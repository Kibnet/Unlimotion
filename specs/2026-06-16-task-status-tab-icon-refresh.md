# Обновление иконки статуса задачи во вкладках

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до подтверждения спеки менять только этот файл; UI-баг фиксировать через reproducing UI test; локальный `AGENTS.override.md` требует добавить/обновить UI-тест и запустить релевантные UI-тесты
- Связанные ссылки: пользовательский баг-репорт в текущем диалоге

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Исправить регрессию, при которой после смены статуса открытой задачи через карточку текущей задачи статусная иконка в деревьях вкладок, прежде всего во вкладке `Все задачи`, остаётся в старом состоянии.

Outcome contract:
- Success means: смена статуса через `CurrentTaskStatusButton` обновляет `TaskItemViewModel.Status`, историю статусов, и все видимые `TaskStatusIcon` для этой же задачи в деревьях вкладок показывают новый статус без перезапуска приложения или ручного пересоздания экрана.
- Итоговый артефакт / output: минимальный кодовый фикс и regression UI test в существующей тестовой поверхности статусов.
- Stop rules: остановиться после падающего reproducing UI test, минимального фикса, зелёного targeted UI test run и финального post-EXEC review; если дефект окажется в другом пользовательском потоке, зафиксировать это в журнале и ограничить реализацию тем же outcome.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion/Views/MainControl.axaml` использует `TaskStatusPicker` в двух местах:
  - строки дерева через `DataTemplate DataType="viewModel:TaskItemViewModel"` и `AutomationId="TaskStatusButton"`;
  - карточка текущей задачи через `CurrentTaskStatusButton`.
- `src/Unlimotion/TaskStatusPicker.cs` содержит внутренний `TaskStatusIcon`, подписывается на `INotifyPropertyChanged` выбранной `TaskItemViewModel` и обновляет `_icon.Status` в `UpdateIcon`.
- `src/Unlimotion.ViewModel/TaskItemViewModel.cs` меняет статус через `StatusOption` и `Status`; `Status` также уведомляет `StatusOption`, `StatusToolTip`, даты статуса и `InProgressElapsed`.
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs` строит табовые коллекции через DynamicData. Корневая коллекция `CurrentAllTasksItems` делает `AutoRefreshOnObservable` по `Status`, но дочерние `TaskWrapperViewModel.SubTasks` строятся лениво через `ChildSelector` и локальные фильтры.
- `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs` уже покрывает внешний вид, flyout и запись истории статусов, но не покрывает сквозной сценарий `CurrentTaskCard -> AllTasksTree icon`.

## 3. Проблема
Одна и та же задача может отображаться одновременно в карточке и в дереве вкладки. При изменении статуса из карточки пользователь ожидает немедленного обновления строки в дереве, но текущие подписки/проекции не гарантируют обновление видимой иконки или состава строк во всех табовых представлениях.

## 4. Цели дизайна
- Разделение ответственности: `TaskStatusPicker` отвечает за визуальное отражение статуса конкретной задачи; табовые проекции отвечают только за состав и сортировку строк.
- Повторное использование: использовать существующий `TaskStatusPicker` и DynamicData-паттерны, не вводить новый статусный UI.
- Тестируемость: сначала добавить UI regression test на реальный пользовательский flow.
- Консистентность: карточка и дерево должны читать один актуальный `TaskItemViewModel.Status`.
- Обратная совместимость: не менять модель данных, enum статусов, локализацию, automation-id и публичный UX-контракт.

## 5. Non-Goals (чего НЕ делаем)
- Не менять статусную модель, разрешённые переходы и бизнес-правила `TaskStatusOption`.
- Не менять внешний вид и размеры `TaskStatusIcon`.
- Не переписывать все табовые DynamicData-проекции.
- Не менять фильтры вкладок, кроме минимального refresh-поведения, если reproducing test покажет, что причина в projection refresh.
- Не добавлять новые persisted поля или миграции.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs` -> reproducing regression UI test для `CurrentTaskStatusButton` и `AllTasksTree`.
- `src/Unlimotion/TaskStatusPicker.cs` -> основной кандидат для фикса, если иконка не получает/не применяет `PropertyChanged(Status)` при уже привязанной задаче.
- `src/Unlimotion.ViewModel/TaskWrapperViewModel.cs` или `src/Unlimotion.ViewModel/MainWindowViewModel.cs` -> допустимый минимальный фикс только если тест докажет, что stale state связан с ленивыми дочерними projection/filter refresh, а не с контролом.

### 6.2 Детальный дизайн
- Поток данных:
  1. Пользователь открывает задачу в `CurrentTaskCard`.
  2. Пользователь выбирает новый статус через `CurrentTaskStatusButton`.
  3. `TaskItemViewModel.StatusOption` меняет `TaskItemViewModel.Status`.
  4. Все `TaskStatusPicker`, связанные с этой же задачей, получают `PropertyChanged(Status)` или актуальный refresh projection.
  5. Видимый `TaskStatusIcon` в `AllTasksTree` показывает новый статус.
- Контракты / API: публичные API не меняются.
- Output contract / evidence rules: regression test должен проверять фактический `TaskStatusIcon.Status` в дереве, а не только ViewModel.
- Visual planning artifact для UI-facing изменений:

```text
Before:
CurrentTaskCard [status: InProgress]    AllTasksTree row [same task status: Prepared]

After:
CurrentTaskCard [status: InProgress]    AllTasksTree row [same task status: InProgress]
```

- UI test video evidence для UI automation задач: fallback. Текущая релевантная поверхность - `Avalonia.Headless`/TUnit UI test; безопасная запись видео из этого runner в репозитории не выявлена в прочитанных тестах. Next-best evidence: падающий/проходящий headless UI test, assertions по визуальному контролу, при необходимости screenshot/log из headless run.
- Границы сохранения поведения: статусный flyout, набор пунктов, tooltips, disabled options, история статусов и сохранение в storage должны остаться как есть.
- Обработка ошибок: если переход статуса заблокирован `CanTransitionToStatus`, поведение остаётся прежним: статус не меняется, toast через notification manager.
- Производительность: не добавлять широкие polling/rebuild циклы; предпочесть точечную подписку или `AutoRefreshOnObservable` по `Status`.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Если статус задачи изменился успешно, все видимые представления этой задачи должны отобразить новый `Status`.
- Если новый статус исключён текущими фильтрами вкладки, строка может исчезнуть из соответствующего списка; если строка остаётся видимой, её иконка обязана соответствовать актуальному статусу.
- Переходы статусов остаются под контролем `TaskItemViewModel.CanTransitionToStatus`.

## 8. Точки интеграции и триггеры
- Триггер: `TaskStatusPicker` menu item click в карточке текущей задачи.
- Обновление: `TaskItemViewModel.PropertyChanged(Status)` и связанные уведомления.
- Интеграция с деревом: `TaskWrapperViewModel.TaskItem` в `AllTasksTree` и шаблон `TaskItemViewModel`.

## 9. Изменения модели данных / состояния
- Новые поля: не применимо.
- Persisted vs calculated: статус и история уже persisted через существующий storage; иконка является calculated UI state.
- Влияние на хранилище: не менять формат хранения.

## 10. Миграция / Rollout / Rollback
- Поведение при первом запуске: не применимо, миграций нет.
- Обратная совместимость: сохранена, так как меняется только синхронизация UI.
- План отката: удалить минимальный фикс и regression test; данные пользователя не затрагиваются.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - При смене статуса через `CurrentTaskStatusButton` статусная иконка той же задачи в `AllTasksTree` обновляется на новый `DomainTaskStatus`.
  - Существующие проверки `TaskStatusPicker_SelectingStatusOption_UpdatesTaskStatusHistory` остаются зелёными.
  - Automation-id существующих controls не меняются.
  - Нет новых persisted fields или миграций.
- Какие тесты добавить/изменить:
  - Добавить тест в `MainControlTaskStatusIconUiTests`, например `CurrentTaskCardStatusChange_UpdatesAllTasksTreeStatusIcon`.
  - Тест должен сначала воспроизвести дефект: открыть `MainControl`, выбрать/зафиксировать задачу, сменить статус через `CurrentTaskStatusButton`, затем найти `TaskStatusButton`/`TaskStatusIcon` для той же задачи в `AllTasksTree` и проверить новый статус.
- Characterization tests / contract checks:
  - Использовать существующий `TaskStatusPicker_SelectingStatusOption_UpdatesTaskStatusHistory` как guard для history/storage contract.
- Visual acceptance:
  - Соответствует fallback storyboard в секции 6.2: карточка и строка дерева показывают один статус.
- UI video evidence:
  - Fallback: headless UI test output. Видео не планируется, потому что текущие `Avalonia.Headless` тесты в прочитанном коде не имеют recorder/video artifact workflow.
- Базовые замеры до/после для performance tradeoff: не применимо, изменение точечное.
- Команды для проверки:
  - Targeted reproducing/fix run:
    `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskStatusIconUiTests/*"`
  - Минимальный build:
    `dotnet build src/Unlimotion.sln`
  - Full test run before final report:
    `dotnet test src/Unlimotion.sln`
- Stop rules для test/retrieval/tool/validation loops:
  - После первого уверенного failing test прекратить расширять repro и перейти к минимальному fix.
  - Если targeted UI suite проходит до фикса, уточнить test scenario, пока он проверяет именно `CurrentTaskCard -> AllTasksTree`.
  - Если full run блокируется внешней средой/таймаутом, зафиксировать targeted evidence и причину невозможности full run.

## 12. Риски и edge cases
- Риск: фикс только в `TaskStatusPicker` не обновит membership дочерних строк, если задача должна исчезнуть из фильтрованной вкладки. Смягчение: regression test должен проверять видимый tree state; если причина в projection refresh, фиксировать DynamicData refresh по `Status`.
- Риск: обновление projection может сбросить выделение/expanded state. Смягчение: не менять selection logic и использовать существующий `TrackExpansionState`.
- Риск: status transition в тесте может быть заблокирован бизнес-правилами. Смягчение: выбрать задачу/статус с разрешённым переходом и явно подготовить условия `IsCanBeCompleted`, dates, criteria.
- Риск: полный `dotnet test src/Unlimotion.sln` может быть долгим или зависнуть локально. Смягчение: сначала targeted UI suite, full run с явной фиксацией результата или объективного blocker.

## 13. План выполнения
1. Добавить reproducing UI test в `MainControlTaskStatusIconUiTests`.
2. Запустить targeted test и убедиться, что он падает на текущем коде.
3. Внести минимальный фикс в `TaskStatusPicker` или DynamicData projection, исходя из фактического failure.
4. Повторить targeted UI test.
5. Запустить `dotnet build src/Unlimotion.sln`.
6. Запустить full test command или зафиксировать объективный blocker.
7. Выполнить post-EXEC review-loop и исправить однозначные findings.

## 14. Открытые вопросы
Нет блокирующих вопросов. Пользовательский текст обрывается после "если", но уже достаточен для корневого сценария: статус меняется в карточке, а вкладка `Все задачи` остаётся stale.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`, context `testing-dotnet`, governance `QUEST`.
- Выполненные требования профиля:
  - UI bug будет покрыт UI regression test.
  - План использует существующий `Avalonia.Headless`/TUnit test pattern.
  - Automation-id сохраняются.
  - Проверочные команды используют `--treenode-filter`, а не VSTest `--filter`.
  - Visual planning artifact задан как lightweight storyboard; video evidence fallback обоснован.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs` | Добавить regression UI test для `CurrentTaskCard -> AllTasksTree` status icon refresh | Воспроизвести и закрепить баг |
| `src/Unlimotion/TaskStatusPicker.cs` | Возможный минимальный фикс подписки/обновления иконки | Если failing test покажет stale icon на уровне контрола |
| `src/Unlimotion.ViewModel/TaskWrapperViewModel.cs` | Возможный минимальный refresh для дочерних wrapper items | Только если причина в ленивой child projection |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Возможный минимальный refresh в табовой projection | Только если причина в tab projection membership |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `CurrentTaskCard` -> `AllTasksTree` | После смены статуса строка дерева может показывать старую иконку | Строка дерева показывает актуальную иконку или исчезает по фильтру |
| UI tests | Нет сквозного теста на этот sync flow | Есть regression UI test |
| Data model | Без изменений | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: принудительно пересоздавать всю вкладку после каждого status change.
- Плюсы: грубо закрывает stale UI.
- Минусы: риск сброса selection/expansion, лишняя работа UI, больше blast radius.
- Почему выбранное решение лучше в контексте этой задачи: точечное обновление через существующие подписки/refresh сохраняет текущий UX и снижает риск регрессий.

- Вариант: биндинг `TaskStatusIcon.Status` напрямую в XAML вместо внутренней подписки `TaskStatusPicker`.
- Плюсы: declarative binding.
- Минусы: текущий control уже инкапсулирует flyout/icon behavior; XAML-only фикс может не покрыть все места создания picker.
- Почему выбранное решение лучше в контексте этой задачи: сначала тест определит фактический stale layer; фикс должен быть на наиболее близком общем слое.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals указаны |
| B. Качество дизайна | 6-10 | PASS | Ответственность, поток данных, правила, интеграции, состояние и rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Есть acceptance, тест-план, риски и пошаговый EXEC-план |
| D. Проверяемость | 14-16 | PASS | Открытых блокеров нет; команды и таблица файлов указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, альтернативы, review и gates зафиксированы |
| F. Соответствие профилю | 20 | PASS | UI automation и .NET/TUnit требования учтены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Сценарий и Non-Goals ограничены статусной иконкой во вкладках |
| 2. Понимание текущего состояния | 5 | Указаны конкретные файлы: XAML, picker, ViewModel, DynamicData projections, тесты |
| 3. Конкретность целевого дизайна | 5 | Зафиксирован поток данных, expected visual state и допустимые места фикса |
| 4. Безопасность (миграция, откат) | 5 | Миграций нет; rollback безопасен |
| 5. Тестируемость | 5 | Есть reproducing UI test, targeted/full команды и stop rules |
| 6. Готовность к автономной реализации | 5 | План даёт порядок TDD и критерии выбора минимального фикса |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-task-status-tab-icon-refresh.md`; instruction stack: `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `session-insights-context`, локальный `AGENTS.override.md`; selected profile: `dotnet-desktop-client` + `ui-automation-testing`; open questions: нет блокирующих; planned changed files: `MainControlTaskStatusIconUiTests.cs`, возможно `TaskStatusPicker.cs`, `TaskWrapperViewModel.cs`, `MainWindowViewModel.cs`
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: прочитаны центральные инструкции, локальный override, `TaskStatusPicker.cs`, фрагменты `MainControl.axaml`, `TaskItemViewModel.cs`, `MainWindowViewModel.cs`, `TaskWrapperViewModel.cs`, `MainControlTaskStatusIconUiTests.cs`
  - Contract pass: спека соблюдает SPEC-first gate, UI visual planning artifact присутствует, video fallback обоснован, UI test обязателен
  - Adversarial risk pass: учтены две возможные причины stale state - control subscription и DynamicData child projection refresh; запрещён широкий rebuild всей вкладки
  - Re-review after fixes / Fix and re-review: исправлений после review не потребовалось
  - Stop decision: PASS, можно ждать фразу `Спеку подтверждаю`
- Evidence inspected: `git status --short` был чистым; существующий тестовый класс содержит релевантные helper methods и TUnit/Avalonia.Headless pattern; `TaskStatusPicker` подписывается на `Status`, но сквозной тест отсутствует
- Depth checklist:
  - Scope drift / unrelated changes: код не менялся; спека ограничивает change set
  - Acceptance criteria: проверяют именно пользовательский flow и визуальный control state
  - Validation evidence: команды запланированы; до EXEC тесты не запускались, потому что сначала нужен approval
  - Unsupported claims: подозрения о root cause помечены как кандидаты и будут подтверждаться falling test
  - Regression / edge case: фильтрованная вкладка и дочерние projections учтены
  - Comments/docs/changelog: changelog/docs не планируются
  - Hidden contract change: automation-id и публичные статусные переходы сохраняются
  - Manual-review challenge: отдельное ревью могло бы спросить про неполную фразу пользователя; спека явно фиксирует, почему это не блокирует корневой сценарий
- No-findings justification: спецификация содержит проверяемый TDD-план, границы и fallback evidence; blocking ambiguity отсутствует

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Не выполнен live repro до approval | Выполнить первым шагом EXEC через падающий UI test | accepted-risk |

- Fixed before continuing: не применимо
- Checks rerun: ручная проверка SPEC Linter/Rubric по owner-документам
- Needs human: требуется подтверждение `Спеку подтверждаю`
- Residual risks / follow-ups: точный слой фикса будет выбран после falling test

### Post-EXEC Review
- Статус: PASS с validation blockers, не связанными с изменённым кодом
- Scope reviewed: `src/Unlimotion.ViewModel/TaskWrapperViewModel.cs`, `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs`, targeted UI test evidence, solution build/full-test blockers
- Decision: можно завершать задачу после финального `git diff --check`
- Review passes:
  - Scope/Evidence pass: PASS; diff ограничен одним production-файлом ViewModel, одним UI-test файлом и этой spec
  - Contract pass: PASS; automation-id не менялись, статусная модель и persisted data не менялись
  - Adversarial risk pass: PASS; root scenario обновляет иконку на текущем коде, reproducing defect найден во вложенной `SubTasks` projection
  - Re-review after fixes / Fix and re-review: PASS; после минимального `AutoRefreshOnObservable` по `Status` ранее падавший nested UI test проходит
  - Stop decision: PASS; targeted UI suite зелёная, broader validation blockers зафиксированы
- Evidence inspected:
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskStatusIconUiTests/CurrentTaskCardStatusChange_UpdatesAllTasksTreeStatusIcon"` -> PASS после корректировки test setup
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskStatusIconUiTests/CurrentTaskCardCompletedStatusChange_RemovesNestedTaskFromAllTasksTreeWhenCompletedIsHidden"` -> FAIL до фикса (`childRemoved` не стал `true`), PASS после фикса
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskStatusIconUiTests/*"` -> PASS, 20/20
  - `dotnet build src\Unlimotion.sln` -> BLOCKED, `NETSDK1147` из-за отсутствующего workload `wasm-tools` для `Unlimotion.Android` и `Unlimotion.iOS`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj` -> BLOCKED/TIMEOUT после 604 секунд без полезного итогового вывода; зависшие test/build процессы остановлены
- Depth checklist:
  - Scope drift / unrelated changes: PASS; никаких unrelated файлов не изменено
  - Acceptance criteria: PASS; card-driven status change обновляет root tree icon, nested completed task удаляется из hidden-completed projection
  - Validation evidence: PASS для релевантной UI поверхности; broader run blockers документированы
  - Unsupported claims: PASS; root cause подтверждён падающим nested UI test
  - Regression / edge case: PASS; покрыты root icon refresh и nested membership refresh
  - Comments/docs/changelog: PASS; changelog не требуется для локального bugfix, spec journal обновлён
  - Hidden contract change: PASS; публичный UX, automation-id, enum и storage format сохранены
  - Manual-review challenge: reviewer мог бы спросить, не слишком ли широк `AutoRefreshOnObservable` для всех child statuses; ответ: refresh точечный по `Status`, применяется только к ленивым child projections и нужен для существующих status filters
- No-findings justification: изменение минимально, воспроизведено падающим UI-тестом до фикса и закрыто targeted suite после фикса

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | environment | Solution build не проходит из-за отсутствующего `wasm-tools` workload для Android/iOS | Не менять код под environment blocker; сообщить в финале | accepted-risk |
| LOW | validation | Полный `Unlimotion.Test` не завершился за 604 секунды | Сообщить timeout и rely on targeted UI suite for changed surface | accepted-risk |

- Fixed before final report: stale child projection fixed via `AutoRefreshOnObservable(task => task.WhenAnyValue(child => child.Status))`
- Checks rerun: targeted root test, targeted nested test, full `MainControlTaskStatusIconUiTests` class
- Validation evidence: 20/20 targeted UI class passed
- Unrelated changes: нет
- Needs human: нет
- Residual risks / follow-ups: full solution validation needs installed `wasm-tools`; full test project may need separate investigation if timeout is not expected in this worktree

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Инструкции и контекст | 0.95 | Нет | Создать спеки и запросить подтверждение | Да | Нет | Центральный QUEST gate запрещает код до утверждения | `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `AGENTS.override.md` |
| SPEC | Code discovery | 0.85 | Точный failure layer будет известен после reproducing test | Ждать `Спеку подтверждаю`, затем добавить падающий UI test | Да | Нет | Прочитаны реальные контролы, ViewModel и тестовый класс; root cause будет подтверждён TDD | `src/Unlimotion/TaskStatusPicker.cs`, `src/Unlimotion.ViewModel/TaskItemViewModel.cs`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.ViewModel/TaskWrapperViewModel.cs`, `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs`, `specs/2026-06-16-task-status-tab-icon-refresh.md` |
| EXEC | Reproducing test | 0.8 | Нужен результат targeted run | Запустить новый UI test через `--treenode-filter` | Нет | Да: пользователь подтвердил спеки | Добавлен сквозной тест `CurrentTaskCardStatusChange_UpdatesAllTasksTreeStatusIcon` по утверждённому сценарию | `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs`, `specs/2026-06-16-task-status-tab-icon-refresh.md` |
| EXEC | Test setup correction | 0.85 | Нужен повторный targeted run | Повторить новый UI test | Нет | Нет | Первый запуск упал до проверяемого дефекта, потому что detail pane не был открыт; добавлен `DetailsAreOpen = true` как в соседних card tests | `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs`, `specs/2026-06-16-task-status-tab-icon-refresh.md` |
| EXEC | Repro refinement | 0.9 | Нужен результат nested targeted run | Запустить nested UI test | Нет | Нет | Root-task сценарий проходит на текущем коде; добавлен nested сценарий для дочерней `SubTasks` projection, где status filter должен удалить completed-задачу из `AllTasksTree` | `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs`, `specs/2026-06-16-task-status-tab-icon-refresh.md` |
| EXEC | Minimal fix | 0.9 | Нужен targeted rerun | Перезапустить падавший nested UI test | Нет | Нет | Падающий nested test подтвердил stale child projection; добавлен `AutoRefreshOnObservable` по `TaskItemViewModel.Status` в `TaskWrapperViewModel.SubTasks` | `src/Unlimotion.ViewModel/TaskWrapperViewModel.cs`, `specs/2026-06-16-task-status-tab-icon-refresh.md` |
| EXEC | Targeted validation | 0.95 | Нет для релевантного UI scope | Запустить build/full validation или зафиксировать blockers | Нет | Нет | Падавший nested test и весь `MainControlTaskStatusIconUiTests` suite проходят после фикса | `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs`, `src/Unlimotion.ViewModel/TaskWrapperViewModel.cs`, `specs/2026-06-16-task-status-tab-icon-refresh.md` |
| EXEC | Broader validation blockers | 0.9 | Требуется установленный `wasm-tools`; full test project timeout требует отдельной диагностики при необходимости | Выполнить diff check и финальный отчёт | Нет | Нет | Solution build заблокирован Android/iOS workload; полный test project остановлен timeout после 604 секунд, targeted UI evidence достаточен для изменённой поверхности | `specs/2026-06-16-task-status-tab-icon-refresh.md` |
