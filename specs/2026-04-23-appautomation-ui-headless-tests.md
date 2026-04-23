# AppAutomation UI и headless тесты карточки задачи

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Unlimotion desktop UI
- Масштаб: medium
- Целевой релиз / ветка: `fix/add-link-from-card`
- Ограничения:
  - До подтверждения спеки код и инфраструктура не меняются.
  - Не менять публичный пользовательский flow добавления связей, кроме добавления стабильных automation ids.
  - Не переносить существующие unit/headless tests из `src/Unlimotion.Test`.
  - Не копировать source код AppAutomation в репозиторий; подключать через NuGet.
- Связанные ссылки:
  - https://github.com/Kibnet/AppAutomation
  - https://api.nuget.org/v3-flatcontainer/appautomation.abstractions/index.json

## 1. Overview / Цель
Добавить автоматизированное покрытие для desktop UI сценария "добавить связь из карточки задачи" через AppAutomation и headless regression test.

Целевой результат:
- появляется канонический AppAutomation слой с общими page object/scenario и двумя runtime-адаптерами: visible UI (`FlaUI`) и `Avalonia.Headless`;
- существующий Avalonia headless test suite получает проверку полного добавления связи из карточки через реальный `MainControl`;
- UI получает недостающие стабильные automation ids для селекторов.

## 2. Текущее состояние (AS-IS)
- Основной desktop UI живёт в `src/Unlimotion/Views/MainControl.axaml` и `MainControl.axaml.cs`.
- Карточка текущей задачи уже содержит четыре relation picker блока:
  - parents: `CurrentItemParentsPicker`;
  - blocking: `CurrentItemBlockedByPicker`;
  - containing: `CurrentItemContainsPicker`;
  - blocked: `CurrentItemBlocksPicker`.
- `TaskRelationPickerViewModel` уже выдаёт automation ids для кнопок и input:
  - `CurrentTask{KindName}RelationAddButton`;
  - `CurrentTask{KindName}RelationAddInput`;
  - `CurrentTask{KindName}RelationAddConfirmButton`;
  - `CurrentTask{KindName}RelationAddCancelButton`.
- Существующий `src/Unlimotion.Test` уже использует `Avalonia.Headless` и TUnit. В `MainControlTreeCommandsUiTests.cs` есть headless UI harness с `HeadlessUnitTestSession.StartNew(typeof(App))`.
- ViewModel tests в `MainWindowViewModelTests.cs` проверяют добавление relations на уровне VM/storage, но не проверяют пользовательский UI flow карточки.
- В AppAutomation `master` и NuGet на момент проверки доступна версия `1.5.2`; README описывает consumer topology:
  - `*.UiTests.Authoring`;
  - `*.UiTests.Headless`;
  - `*.UiTests.FlaUI`;
  - `*.AppAutomation.TestHost`.

## 3. Проблема
Сценарий добавления связи из карточки задачи не закреплён end-to-end UI тестом: есть VM/storage проверки, но нет стабильной проверки, что пользователь может открыть карточку, увидеть relation controls и выполнить добавление связи через headless UI.

## 4. Цели дизайна
- Разделение ответственности: page object/scenario в Authoring, runtime launch в Headless/FlaUI, seed/launch logic в TestHost.
- Повторное использование: общий AppAutomation сценарий должен запускаться и в visible UI, и в headless runtime.
- Тестируемость: полный relation add проверяется headless regression test без видимого UI и без зависимости от локального профиля пользователя.
- Консистентность: использовать существующие fixture/snapshot данные и TUnit стиль проекта.
- Обратная совместимость: не менять storage format, business logic, persisted settings и пользовательские тексты.

## 5. Non-Goals (чего НЕ делаем)
- Не переписывать текущий `MainControlTreeCommandsUiTests` harness на AppAutomation.
- Не внедрять AppAutomation recorder в приложение.
- Не менять логику `TaskRelationPickerViewModel`.
- Не добавлять новые пользовательские элементы, кроме automation ids.
- Не делать отдельный CI workflow в этой задаче.
- Не добавлять AppAutomation source/submodule.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainWindow.axaml` -> automation id root window.
- `src/Unlimotion/Views/MainControl.axaml` -> automation ids для main tabs, visible trees, details toggle и relation trees.
- `tests/Unlimotion.AppAutomation.TestHost` -> запуск desktop/headless AUT на seeded test data.
- `tests/Unlimotion.UiTests.Authoring` -> AppAutomation page object и общий сценарий карточки relation picker.
- `tests/Unlimotion.UiTests.Headless` -> AppAutomation headless runner.
- `tests/Unlimotion.UiTests.FlaUI` -> AppAutomation visible UI/FlaUI runner для Windows.
- `src/Unlimotion.Test/MainControlRelationPickerUiTests.cs` -> полный Avalonia.Headless сценарий добавления связи через `AutoCompleteBox` и confirm.
- `src/Unlimotion.sln` -> включение новых test projects, чтобы обычный build/test видел AppAutomation слой.

### 6.2 Детальный дизайн
- AppAutomation package references:
  - `AppAutomation.Abstractions` `1.5.2`;
  - `AppAutomation.Authoring` `1.5.2`;
  - `AppAutomation.TUnit` `1.5.2`;
  - `AppAutomation.TestHost.Avalonia` `1.5.2`;
  - `AppAutomation.Avalonia.Headless` `1.5.2`;
  - `AppAutomation.FlaUI` `1.5.2`.
- AppAutomation Authoring scenario:
  - запускает seeded app state;
  - выбирает known task в `AllTasksTree`;
  - открывает details pane, если нужно;
  - проверяет доступность `CurrentTaskTitleTextBox`;
  - открывает parents relation picker через `CurrentTaskParentsRelationAddButton`;
  - проверяет раскрытие picker по `CurrentTaskParentsRelationAddCancelButton`;
  - закрывает picker через `CurrentTaskParentsRelationAddCancelButton`.
- Полный add-flow headless regression:
  - использует существующий `MainWindowViewModelFixture`;
  - открывает `MainControl` в `HeadlessUnitTestSession`;
  - выбирает задачу `Blocked task 7`;
  - кликает add в parents relation section;
  - вводит query в native `AutoCompleteBox`;
  - выбирает/подтверждает `Task 1`;
  - проверяет storage: у текущей задачи появился parent `RootTask1Id`, а у parent появился child текущей задачи.
- AppAutomation visible UI (`FlaUI`) не проверяет запись relation через `AutoCompleteBox`, потому что первый слой должен быть устойчивым smoke/selector flow. Полная запись покрывается headless native тестом, где можно безопасно работать с Avalonia control instance.
- Ошибки AppAutomation должны давать диагностические артефакты framework-а. Дополнительно не требуется screenshot/trace инфраструктура.
- Производительность: тесты используют существующие snapshots и temp folders; запуск FlaUI ограничен smoke scenario, чтобы не удлинять suite без необходимости.

## 7. Бизнес-правила / Алгоритмы
- Для `Parents` relation:
  - current task получает candidate id в `ParentTasks`;
  - candidate task получает current task id в `ContainsTasks`;
  - self/cycle/existing relation candidates остаются обязанностью существующего `TaskRelationPickerViewModel` и `TaskTreeManager`.
- Тестовые данные берутся из существующих snapshots:
  - current task: `Blocked task 7`; фактический константный id брать из fixture;
  - parent candidate: `Task 1` / `RootTask1Id`.

## 8. Точки интеграции и триггеры
- XAML selectors:
  - `AutomationProperties.AutomationId` на root window, `TabControl`, `AllTasksTree`, relation trees и details toggle.
- AppAutomation launch:
  - headless: `HeadlessUnitTestSession.StartNew(typeof(App))` + `HeadlessRuntime.SetSession`;
  - visible UI: `AvaloniaDesktopLaunchHost.CreateLaunchOptions` для `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj`.
- Test data:
  - temp copy of `src/Unlimotion.Test/Snapshots`;
  - temp `TestSettings.json` with task folder path rewritten.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Формат task files не меняется.
- Новое состояние только test-time temp directories/configs.
- UI state меняется только через уже существующие bindings relation picker.

## 10. Миграция / Rollout / Rollback
- Первый запуск приложения не меняется.
- Rollout: новые тестовые проекты включаются в solution; существующий product code получает только automation ids.
- Rollback:
  - удалить новые `tests/Unlimotion.*UiTests*` и TestHost проекты;
  - удалить новые automation ids из XAML;
  - удалить новый headless regression test;
  - убрать проекты из `src/Unlimotion.sln`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. В solution есть AppAutomation test topology для Unlimotion.
  2. AppAutomation Authoring scenario запускается через Headless runner.
  3. AppAutomation Authoring scenario запускается через visible UI/FlaUI runner на Windows.
  4. Stable selectors не завязаны на display text, кроме выбора seeded tree item по тестовым данным.
  5. Headless regression test подтверждает, что relation можно добавить из карточки задачи через UI controls.
  6. Тесты не пишут в пользовательские настройки и не используют постоянные пользовательские папки.
  7. `dotnet build` и `dotnet test` проходят локально.
- Какие тесты добавить/изменить:
  - `Unlimotion.UiTests.Authoring`: общий scenario `Card_relation_picker_can_be_opened_from_task_card`.
  - `Unlimotion.UiTests.Headless`: inherited headless run.
  - `Unlimotion.UiTests.FlaUI`: inherited visible UI run.
  - `Unlimotion.Test`: native headless regression `TaskCardRelationPicker_AddParentFromCard_UpdatesStorage`.
- Команды для проверки:
  - `dotnet build src/Unlimotion.sln /m:1 /nodeReuse:false /p:UseSharedCompilation=false`
  - `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build`
  - `dotnet test tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug --no-build`
  - `src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.exe --filter-uid Unlimotion.Test.MainControlRelationPickerUiTests.1.1.TaskCardRelationPicker_AddParentFromCard_UpdatesStorage.1.1.0`
  - `dotnet test src/Unlimotion.sln --no-build -- --timeout 900s --no-progress`

## 12. Риски и edge cases
- `AutoCompleteBox` не является первым-class primitive в AppAutomation 1.5.2; полный ввод relation query лучше закрепить native Avalonia.Headless тестом.
- FlaUI тесты Windows-only; проект должен быть изолирован так, чтобы build не ломал non-Windows окружения без необходимости.
- Static state в `App` и `TaskItemViewModel` может протекать между UI тестами; test host должен использовать непараллельный запуск и cleanup temp folders.
- Visible UI запуск может быть медленнее обычных unit tests; сценарий должен оставаться smoke-level.
- Стабильность selector-ов зависит от `AutomationProperties.AutomationId`, поэтому нельзя привязываться к локализованному тексту кнопок.

## 13. План выполнения
1. Добавить automation ids в `MainWindow.axaml` и `MainControl.axaml`.
2. Создать AppAutomation TestHost с seeded temp config/tasks.
3. Создать Authoring page object и общий scenario для карточки relation picker.
4. Создать Headless runner и session hooks.
5. Создать FlaUI runner для visible UI smoke.
6. Добавить native Avalonia.Headless regression test полного add-parent flow.
7. Включить новые проекты в `src/Unlimotion.sln`.
8. Запустить targeted tests.
9. Запустить полный `dotnet build` и `dotnet test`.
10. Выполнить post-EXEC review и исправить найденные критичные замечания.

## 14. Открытые вопросы
Блокирующих вопросов нет. Если FlaUI окажется нестабильным в локальном окружении, допустимый fallback в рамках этой спеки: оставить проект и scenario, но явно пометить run как Windows-only/diagnostic и сохранить обязательным Headless runner + native headless regression.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля:
  - UI flow покрывается автоматическими тестами.
  - Используются стабильные automation ids.
  - Headless UI smoke/regression suite добавляется и запускается перед завершением.
  - Не добавляются длительные синхронные операции в UI thread.
  - Перед завершением планируется `dotnet build` и `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainWindow.axaml` | Добавить automation id root window | Root selector для AppAutomation |
| `src/Unlimotion/Views/MainControl.axaml` | Добавить automation ids для tabs, trees, details toggle, relation trees | Stable selectors |
| `tests/Unlimotion.AppAutomation.TestHost/*` | Новый launch/test data host | Общая bootstrap логика |
| `tests/Unlimotion.UiTests.Authoring/*` | Page object и shared scenario | Runtime-neutral UI сценарий |
| `tests/Unlimotion.UiTests.Headless/*` | Headless adapter runner | Быстрые headless проверки |
| `tests/Unlimotion.UiTests.FlaUI/*` | Visible UI adapter runner | UI/FlaUI покрытие |
| `src/Unlimotion.Test/MainControlRelationPickerUiTests.cs` | Native Avalonia.Headless regression | Полный add relation flow |
| `src/Unlimotion.sln` | Добавить новые projects | Build/test discovery |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Relation add from card | VM/storage tests без полного UI add flow | Native headless UI regression полного add flow |
| UI automation topology | Только `src/Unlimotion.Test` | AppAutomation Authoring + Headless + FlaUI + TestHost |
| Selectors | Часть controls имеет ids, часть только `x:Name` | Stable automation ids для тестируемых anchors |
| Visible UI smoke | Нет | FlaUI smoke на том же shared scenario |

## 18. Альтернативы и компромиссы
- Вариант: ограничиться существующим `Avalonia.Headless`.
  - Плюсы: меньше файлов, быстрее внедрение.
  - Минусы: не выполняет запрос на AppAutomation и не даёт runtime-neutral scenarios.
- Вариант: полностью покрыть add relation только AppAutomation.
  - Плюсы: единый DSL.
  - Минусы: `AutoCompleteBox` не поддержан как primitive в AppAutomation 1.5.2; потребовалась бы отдельная adapter/bridge работа вне масштаба.
- Выбранный вариант:
  - AppAutomation закрепляет canonical UI/headless topology и selector smoke;
  - native Avalonia.Headless закрывает полный regression add-flow там, где нужен прямой доступ к Avalonia controls;
  - решение минимизирует риск и сохраняет расширяемость для будущего AutoCompleteBox adapter.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, алгоритмы, state и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Нет persisted migration; риски AutoCompleteBox/FlaUI/static state выделены. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, тесты и команды проверки перечислены. |
| E. Готовность к автономной реализации | 17-19 | PASS | План этапов и компромисс AppAutomation/native headless определены. |
| F. Соответствие профилю | 20 | PASS | Desktop UI и UI automation требования покрыты. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Цель ограничена UI/headless тестами relation flow. |
| 2. Понимание текущего состояния | 5 | Зафиксированы существующие XAML, ViewModel, tests и AppAutomation topology. |
| 3. Конкретность целевого дизайна | 5 | Указаны projects, selectors, scenarios и package versions. |
| 4. Безопасность (миграция, откат) | 5 | Persisted data не меняется, rollback прямой. |
| 5. Тестируемость | 5 | Есть targeted и full commands, acceptance criteria и regression coverage. |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов, порядок реализации определён. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлен явный fallback по `AutoCompleteBox`, уточнён Windows-only риск FlaUI, добавлен cleanup/static state риск.
- Что осталось на решение пользователя: требуется только утверждение спеки фразой ниже.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | AppAutomation UI/headless test design | 0.86 | Практическая проверка FlaUI после реализации | Дождаться подтверждения спеки | Да | Да, требуется фраза `Спеку подтверждаю` | Центральные инструкции требуют SPEC-first и запрещают менять код до approval | `specs/2026-04-23-appautomation-ui-headless-tests.md` |
| EXEC | UI selectors + AppAutomation topology + native headless regression | 0.78 | Результат компиляции новых пакетов и XAML | Запустить targeted build/tests | Нет | Да, пользователь подтвердил spec | Добавлены stable selectors, TestHost/Authoring/Headless/FlaUI проекты и полный relation add regression через Avalonia.Headless | `src/Unlimotion/Views/MainWindow.axaml`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.Test/MainControlRelationPickerUiTests.cs`, `tests/Unlimotion.*`, `src/Unlimotion.sln` |
| EXEC | Validation fixes | 0.84 | Нет | Запустить полный build/test | Нет | Нет | FlaUI не видит root `AutoCompleteBox` как стабильный UIA element, поэтому AppAutomation smoke ждёт cancel-состояние; Authoring исключён из test discovery; старые full-suite blockers стабилизированы коротким temp path и явной локализацией | `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`, `tests/Unlimotion.UiTests.Authoring/Unlimotion.UiTests.Authoring.csproj`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs` |
| EXEC | Final validation | 0.92 | Нет | Выполнить post-EXEC review | Нет | Нет | Targeted Headless/FlaUI/native tests, full solution build и full solution test прошли; для solution test нужен `--timeout 900s`, потому что 300s отменял `Unlimotion.Test` без failures | `src/Unlimotion.sln`, `tests/Unlimotion.UiTests.Headless`, `tests/Unlimotion.UiTests.FlaUI`, `src/Unlimotion.Test` |
| EXEC | Post-EXEC review | 0.90 | Нет | Сообщить результат | Нет | Нет | Review не выявил блокирующих замечаний; automation startup hook активен только при env vars, TestHost использует temp data/config и cleanup, Authoring не участвует в test discovery как пустой assembly | Все изменённые файлы |
