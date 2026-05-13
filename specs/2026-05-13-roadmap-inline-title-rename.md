# Переименование задач в roadmap

## 0. Метаданные
- Тип (профиль): delivery-task; profiles: `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex / пользователь
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка репозитория
- Ограничения: до подтверждения спеки менять только этот файл; локальный `AGENTS.override.md` требует UI-тесты для UI behavior; не трогать сторонние изменения, включая текущее удаление `AGENTS.md` в git status
- Связанные ссылки: `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `AGENTS.override.md`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
В roadmap-графе нужно включить переименование задачи теми же пользовательскими жестами, что и в остальных вкладках дерева задач:
- повторный клик по заголовку той же задачи после паузы;
- `F2` для текущей выбранной задачи.

Outcome contract:
- Success means: в roadmap можно выбрать задачу, открыть inline-редактор названия через повторный клик по заголовку или через `F2`, изменить `TaskItemViewModel.Title`, и текст узла обновляется без лишней перестройки roadmap-графа.
- Итоговый артефакт / output: минимальные изменения в `GraphControl`/roadmap XAML и regression UI-тест в существующем headless test suite.
- Stop rules: остановиться после прохождения targeted UI-теста, `dotnet build` и полного тестового прогона; если полный прогон технически невозможен, зафиксировать причину и выполненную next-best проверку.

## 2. Текущее состояние (AS-IS)
- Inline-переименование для обычных вкладок живёт в `src/Unlimotion/Views/MainControl.axaml.cs`: `InlineTaskTitleText_OnPointerPressed`, `TaskTree_OnKeyDown`, `FocusCurrentTaskInlineTitleEditor`, `CreateInlineTitleEditor`.
- В обычных деревьях повторный клик по заголовку работает не как быстрый double-click, а как второй клик по той же задаче после `InlineTitleRepeatedClickDelay = 500 ms`; быстрый повторный клик игнорируется для inline-edit, чтобы не конфликтовать с double tap.
- `F2` в деревьях обрабатывается на `TreeView.KeyDown` и открывает `InlineTaskTitleTextBox` для `CurrentTaskItem`.
- Roadmap живёт в `src/Unlimotion/Views/GraphControl.axaml` и `src/Unlimotion/Views/GraphControl.axaml.cs`.
- Roadmap-узел сейчас отображает `TaskItem.GetAllEmoji`, repeater marker и `TaskItem.TitleWithoutEmoji` отдельными элементами. Заголовок roadmap не имеет inline-редактора.
- `GraphControl.RoadmapEditor_KeyDown` уже обрабатывает хоткеи roadmap: `Ctrl+Enter`, `Shift+Enter`, `Ctrl+Tab`, `F/U/T`, `R`, но не `F2`.
- Клик по roadmap-узлу вызывает `SelectRoadmapTask`; double tap вызывает `ToggleRoadmapTaskDetails`.
- Есть существующие headless UI-тесты в `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, включая `RoadmapGraph_TitleRename_UpdatesTextWithoutRebuildingMap`, который проверяет, что изменение `Title` обновляет текст без rebuild.

## 3. Проблема
Roadmap не поддерживает тот же inline-flow переименования задач, который уже доступен в остальных вкладках: пользователь не может начать переименование выбранной задачи через `F2` или повторный клик по названию прямо в roadmap.

## 4. Цели дизайна
- Разделение ответственности: оставить roadmap-specific обработку в `GraphControl`, не менять ViewModel и storage.
- Повторное использование: повторить проверенную модель обычных вкладок для задержанного повторного клика и `F2`, не переносить весь tree-specific код.
- Тестируемость: добавить стабильные automation id для roadmap title text/editor и покрыть сценарии Avalonia.Headless UI-тестом.
- Консистентность: быстрый double tap по roadmap-узлу продолжает открывать/закрывать детали, а delayed repeated title click открывает rename.
- Обратная совместимость: не менять публичные API, формат данных, storage и существующие команды создания задач.

## 5. Non-Goals (чего НЕ делаем)
- Не менять алгоритм layout/build roadmap-графа.
- Не менять модель данных `TaskItemViewModel`, `TaskItem`, storage или relation index.
- Не унифицировать весь inline-edit код `MainControl` и `GraphControl` через новый общий компонент.
- Не менять UX обычных вкладок дерева задач.
- Не добавлять контекстное меню или отдельную кнопку rename.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/GraphControl.axaml` -> добавить стабильные automation id для title surface/title text roadmap-узла; добавить стиль inline-редактора, совместимый с текущим layout.
- `src/Unlimotion/Views/GraphControl.axaml.cs` -> управлять состоянием roadmap inline-edit, repeated title click, `F2`, созданием/фокусом/удалением `TextBox`.
- `src/Unlimotion.Test/RoadmapGraphUiTests.cs` -> добавить regression UI-тест пользовательского сценария roadmap rename.

### 6.2 Детальный дизайн
- В roadmap title добавить прозрачный `Grid`-surface с `AutomationProperties.AutomationId="RoadmapInlineTaskTitleSurface"` и вложенный `TextBlock` с `AutomationProperties.AutomationId="RoadmapInlineTaskTitleTextBlock"`.
- Pointer trigger подключить в `GraphControl` через tunnel `AddHandler(PointerPressedEvent, RoadmapInlineTitleText_OnPointerPressed, RoutingStrategies.Tunnel, true)`, чтобы не зависеть от hit-testing особенностей Nodify в headless/real UI.
- В `GraphControl` добавить состояние, аналогичное `MainControl`:
  - активный roadmap inline editor;
  - последний roadmap title click task id;
  - время последнего click;
  - константа задержки `500 ms`.
- Для delayed repeated click:
  - принимать только левый клик без модификаторов по title text;
  - первый клик выбирает задачу через существующий `SelectRoadmapTask`;
  - быстрый повторный click/double tap не создаёт редактор, чтобы существующий `DoubleTapped` мог открыть детали;
  - повторный клик по тому же title после задержки создаёт и фокусирует editor.
  - `e.Handled` выставлять только когда editor реально создан/сфокусирован; первый клик и быстрый повторный click/double tap не должны быть съедены title-handler, чтобы parent handlers roadmap продолжили отвечать за selection, drag threshold и details toggle.
- Для `F2`:
  - в `RoadmapEditor_KeyDown` при `KeyModifiers.None && Key.F2` открыть editor для `MainWindowViewModel.CurrentTaskItem`;
  - не срабатывать, если фокус уже внутри `TextBox`, `AutoCompleteBox` или `NumericUpDown` через существующий `IsTextInputEventSource`.
- Editor:
  - создаётся динамически рядом с title text в parent panel roadmap-узла;
  - `DataContext = TaskItemViewModel`;
  - `TextBox.Text` two-way binding к `TaskItemViewModel.Title` с `UpdateSourceTrigger.PropertyChanged`;
  - automation id `RoadmapInlineTaskTitleTextBox`;
  - при фокусе выделяет весь текст, при потере фокуса удаляется из visual tree.
- Ошибки:
  - если selected task отсутствует, visual не найден или node не видим, `F2` ничего не делает и не помечает событие handled.
- Производительность:
  - изменение `Title` должно использовать существующее property binding обновление UI; rebuild roadmap-графа не требуется.

## 7. Бизнес-правила / Алгоритмы (если есть)
- `F2` без модификаторов запускает rename только для текущей выбранной roadmap-задачи.
- Повторный клик по тому же title после интервала >= 500 ms запускает rename.
- Быстрый double tap по roadmap-узлу сохраняет текущее поведение открытия/закрытия details.
- Rename редактирует полный `TaskItemViewModel.Title`; отображение roadmap после редактирования остаётся через текущие `GetAllEmoji` и `TitleWithoutEmoji`.

## 8. Точки интеграции и триггеры
- `GraphControl.RoadmapEditor_KeyDown` -> новый триггер `F2`.
- `RoadmapInlineTitleText_OnPointerPressed` -> новый tunnel pointer trigger для delayed repeated title click по roadmap title surface/title text.
- `TextBox.LostFocus` -> cleanup активного editor.
- Existing binding `TaskItemViewModel.Title` / `TitleWithoutEmoji` -> обновление текста узла.

## 9. Изменения модели данных / состояния
- Persisted модель данных: без изменений.
- Новое UI-only состояние в `GraphControl`: активный editor и состояние последнего клика.
- Влияние на хранилище: только обычное обновление `TaskItemViewModel.Title`, как в существующих вкладках.

## 10. Миграция / Rollout / Rollback
- Поведение при первом запуске: без миграций.
- Обратная совместимость: существующие задачи и roadmap projection не меняются.
- Rollback: удалить добавленные handlers/style/state и новый UI-тест; данные пользователей не затрагиваются.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В roadmap после выбора задачи `F2` открывает inline editor для выбранной задачи.
  - В roadmap повторный клик по title той же задачи после паузы открывает inline editor.
  - Быстрый click/double tap не открывает inline editor и не ломает существующее открытие details.
  - Изменение текста editor обновляет `TaskItemViewModel.Title`.
  - Rename не вызывает rebuild roadmap-графа сверх уже существующего behavior для title update.
- Какие тесты добавить/изменить:
  - Добавить в `RoadmapGraphUiTests` headless UI-test по аналогии с `TreeCommandUi_InlineTitleEdit_CreatesEditorOnlyForF2OrRepeatedTitleClick`.
  - При необходимости расширить helper-методы поиска roadmap title/editor по automation id.
- Characterization tests / contract checks:
  - Оставить существующий `RoadmapGraph_TitleRename_UpdatesTextWithoutRebuildingMap`.
  - Новый тест должен проверить хотя бы один ввод через editor и `F2`.
- Базовые замеры до/после для performance tradeoff: не применимо, изменение не затрагивает layout algorithm; проверяется отсутствие лишнего rebuild.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_InlineTitleEdit_CreatesEditorForF2OrRepeatedTitleClick" --maximum-parallel-tests 1 --no-progress`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_TitleRename_UpdatesTextWithoutRebuildingMap" --maximum-parallel-tests 1 --no-progress`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`
- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted UI-тест падает, исправлять до прохождения.
  - Если full suite падает из-за unrelated known failure или environment issue, зафиксировать конкретный failure и выполнить максимально близкий проектный набор проверок.

## 12. Риски и edge cases
- Риск конфликта с double tap details: mitigated by preserving fast repeated click path and testing no editor on rapid click.
- Риск, что `F2` сработает из text input: mitigated by existing `IsTextInputEventSource`.
- Риск невидимого editor из-за overlay/layout: mitigated by headless test checking visibility/focus and stable automation id.
- Риск лишнего graph rebuild после rename: mitigated by existing and targeted tests around graph update count.
- Риск пересечения automation id с обычными вкладками: использовать roadmap-specific ids.

## 13. План выполнения
1. Добавить failing regression UI-тест в `RoadmapGraphUiTests` для repeated title click и `F2`.
2. В `GraphControl.axaml` добавить roadmap title surface/text automation ids и стиль editor.
3. В `GraphControl.axaml.cs` реализовать roadmap inline editor lifecycle и `F2` route.
4. Запустить targeted UI-тесты и исправить найденные проблемы.
5. Запустить `dotnet build src/Unlimotion.sln` и `dotnet test src/Unlimotion.sln`.
6. Выполнить post-EXEC review и при необходимости повторить затронутые проверки.

## 14. Открытые вопросы
Нет блокирующих вопросов. Продуктовое поведение задано пользователем: roadmap должен соответствовать остальным вкладкам.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - Изменение ограничено Avalonia UI behavior.
  - UI flow покрывается существующим Avalonia.Headless test suite.
  - Стабильные automation id добавляются для roadmap title/editor.
  - Перед завершением должны быть запущены targeted UI-тесты, build и полный тестовый прогон.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/GraphControl.axaml` | Добавить title surface/text automation ids и стиль roadmap inline editor | UI target для repeated title click и тестовых селекторов |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Добавить lifecycle inline editor, click state и `F2` handling | Реализовать rename behavior в roadmap |
| `src/Unlimotion.Test/RoadmapGraphUiTests.cs` | Добавить/расширить headless UI regression test | Зафиксировать пользовательский сценарий и локальный override по UI testing |
| `specs/2026-05-13-roadmap-inline-title-rename.md` | Рабочая спецификация | Central QUEST gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Roadmap `F2` | Не запускает rename; доступны только roadmap hotkeys | Запускает inline rename выбранной задачи |
| Roadmap repeated title click | Выбор/double tap details, без rename | Delayed repeated click по title открывает rename |
| Быстрый double tap | Открывает/закрывает details | Сохраняет открытие/закрытие details |
| Rename update | Только через details/другие вкладки | Можно редактировать прямо в roadmap, binding обновляет node text |

## 18. Альтернативы и компромиссы
- Вариант: вынести общий inline title editor helper для `MainControl` и `GraphControl`.
- Плюсы: меньше дублирования.
- Минусы: больше blast radius и риск регрессий в обычных вкладках.
- Почему выбранное решение лучше в контексте этой задачи: изменение маленькое и roadmap-specific, пользователь просит parity, а не рефакторинг shared UI infrastructure.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals описаны проверяемо |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, правила, state и rollback раскрыты |
| C. Безопасность изменений | 11-13 | PASS | Тест-план, edge cases и этапы выполнения покрывают UI-риск |
| D. Проверяемость | 14-16 | PASS | Открытых вопросов нет, acceptance criteria и файлы указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, альтернативы и review зафиксированы |
| F. Соответствие профилю | 20 | PASS | UI automation и .NET desktop требования отражены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один конкретный UI parity bugfix с явными Non-Goals |
| 2. Понимание текущего состояния | 5 | Указаны текущие handlers MainControl и GraphControl, существующие тесты |
| 3. Конкретность целевого дизайна | 5 | Описаны triggers, state, editor binding и cleanup |
| 4. Безопасность (миграция, откат) | 5 | Модель данных не меняется, rollback простой |
| 5. Тестируемость | 5 | Есть targeted UI-тесты, build и full test commands |
| 6. Готовность к автономной реализации | 5 | План без блокирующих вопросов |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: уточнено сохранение fast double tap details, добавлены roadmap-specific automation ids, проверка отсутствия лишнего rebuild, корректные TUnit `--treenode-filter` команды и контракт `e.Handled` для roadmap title click.
- Что осталось на решение пользователя: требуется подтверждение спеки фразой ниже.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор инструкций и контекста | 0.95 | Нет | Создать рабочую спеку | Да, для перехода в EXEC | Будет запрошено в ответе пользователю | Central stack требует SPEC gate до кодовых правок | `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `AGENTS.override.md`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion/Views/GraphControl.axaml.cs`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.Test/RoadmapGraphUiTests.cs` |
| SPEC | Подготовка спеки | 0.95 | Нет | Ожидать подтверждение пользователя | Да, нужна фраза `Спеку подтверждаю` | Да, этот документ содержит Approval gate | Реализация понятна, но код менять нельзя до approval | `specs/2026-05-13-roadmap-inline-title-rename.md` |
| SPEC | Исправление review-находок | 0.97 | Нет | Ожидать подтверждение пользователя | Да, нужна фраза `Спеку подтверждаю` | Да, пользователь попросил исправить findings | Уточнены команды проверки, чтобы targeted run не проходил с 0 tests, и event-routing контракт для roadmap title click | `specs/2026-05-13-roadmap-inline-title-rename.md` |
| EXEC | Failing UI regression test | 0.92 | Нужно подтвердить красный статус до production fix | Запустить targeted тест | Нет | Да, пользователь подтвердил спеку | Добавлен headless сценарий repeated title click и `F2` для roadmap inline rename | `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, `specs/2026-05-13-roadmap-inline-title-rename.md` |
| EXEC | Реализация roadmap inline rename | 0.94 | Нет | Запустить roadmap regression suite | Нет | Нет | Добавлен title surface с automation id, inline editor lifecycle, delayed repeated click и `F2` route; тест использует routed pointer event из-за нестабильного raw hit-testing Nodify в headless | `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml.cs`, `src/Unlimotion.Test/RoadmapGraphUiTests.cs` |
| EXEC | Targeted UI verification | 0.95 | Нет | Запустить полный `RoadmapGraphUiTests` | Нет | Нет | Прошли новый `RoadmapGraph_InlineTitleEdit_CreatesEditorForF2OrRepeatedTitleClick` и существующий `RoadmapGraph_TitleRename_UpdatesTextWithoutRebuildingMap` | `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, test runner |
| EXEC | Roadmap suite verification | 0.96 | Нет | Запустить build/full validation | Нет | Нет | `RoadmapGraphUiTests` прошёл полностью: 35/35, включая selection, double tap, drag, right-drag pan, create hotkeys и новый inline rename | `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, test runner |
| EXEC | Post-EXEC polish | 0.95 | Нужно повторить проверки после маленькой правки handler ordering | Запустить targeted build/test | Нет | Нет | Обработчик roadmap title click теперь делает ранние отсечки до fallback обхода visual tree; spec синхронизирован с tunnel-handler реализацией | `src/Unlimotion/Views/GraphControl.axaml.cs`, `specs/2026-05-13-roadmap-inline-title-rename.md` |
| EXEC | Final targeted verification | 0.96 | Нет | Подготовить финальный ответ | Нет | Нет | После post-review правки прошли: `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore -v:minimal`, новый inline rename тест, `RoadmapGraph_TitleRename_UpdatesTextWithoutRebuildingMap`, полный `RoadmapGraphUiTests` 35/35; `git diff --check` без whitespace errors, только предупреждения LF->CRLF | `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml.cs`, `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, test/build commands |
| EXEC | Full validation limitations | 0.88 | Нет для scope roadmap; есть unrelated/global blockers вне изменённого кода | Подготовить финальный ответ | Нет | Нет | Solution-level build ранее зависал/истекал по лимиту, а полный `Unlimotion.Test` падал вне roadmap на existing UI/threading сценариях; relevant UI coverage для изменённого roadmap flow выполнена и зелёная | solution build/full test commands |
| EXEC | Post-polish verification | 0.97 | Нет | Проверить финальный diff | Нет | Нет | `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore -p:UseSharedCompilation=false -p:UsedAvaloniaProducts= /nr:false -v:minimal` прошёл; новый targeted UI-test прошёл после handler guard, включая coordinate guard для кликов внутри editor | `src/Unlimotion/Views/GraphControl.axaml.cs`, `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, test runner |
| EXEC | Final roadmap verification | 0.97 | Full solution/full suite имеют внешние caveats, описанные в финальном ответе | Завершить ответ пользователю | Нет | Нет | Финальный `RoadmapGraphUiTests` прошёл 35/35 после последней кодовой правки; `git diff --check` показал только LF->CRLF warnings для затронутых файлов | `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml.cs`, `specs/2026-05-13-roadmap-inline-title-rename.md` |
