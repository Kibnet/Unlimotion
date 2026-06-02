# Восстановление выделенной задачи после очистки поиска

## 0. Метаданные
- Тип (профиль): delivery-task; dotnet-desktop-client + ui-automation-testing
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: central QUEST требует approval-gate до EXEC; локальный `AGENTS.override.md` требует UI regression test и запуск релевантных UI-тестов.
- Связанные ссылки: Не применимо.

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Исправить сценарий: пользователь вводит строку поиска, выбирает найденную задачу, очищает поиск и ожидает увидеть эту же задачу выделенной и проскролленной в `All Tasks` дереве.

Outcome contract:
- Success means: после очистки `Search.SearchText` текущая задача снова находится в `CurrentAllTasksItems`, ее родители раскрыты, `CurrentAllTasksItem` указывает на актуальный wrapper из полного дерева, `AllTasksTree.SelectedItem` визуально выделен, а selected `TreeViewItem` материализован в видимой области `AllTasksTree`.
- Итоговый артефакт / output: исправление selection refresh для `AllTasksTree` и Avalonia.Headless UI regression test.
- Stop rules: сначала reproducing UI test, затем fix; завершать EXEC только после targeted UI test и доступной build/test validation либо с явным блокером окружения.

## 2. Текущее состояние (AS-IS)
- `TreeView.SelectedItem` в `AllTasksTree` привязан к `MainWindowViewModel.CurrentAllTasksItem`.
- Выбор задачи в дереве обновляет `CurrentTaskItem` через подписку на `CurrentAllTasksItem`.
- При поиске DynamicData-фильтры пересобирают wrapper-ы в `CurrentAllTasksItems`.
- `ExpandParentNodesForTask(CurrentTaskItem)` умеет раскрывать родителей и переустанавливать `CurrentAllTasksItem`, чтобы `AutoScrollToSelectedItem` сработал.
- Сейчас эта логика вызывается при переключении режима и при явных операциях выбора, но не гарантированно вызывается после очистки поискового фильтра.
- `SelectCurrentTask()` в ветке `AllTasksMode` пропускает обновление, если старый `CurrentAllTasksItem?.TaskItem` равен `CurrentTaskItem`, даже если wrapper уже удален из отфильтрованного дерева и нужен новый wrapper из полного дерева.

## 3. Проблема
Корневая проблема: после пересборки дерева поиском selection хранит task identity, но не гарантирует актуальный wrapper instance из текущего `CurrentAllTasksItems`; из-за этого `TreeView` не получает новое `SelectedItem`, родители не раскрываются, и автоскролл не доводит задачу в видимую область.

## 4. Цели дизайна
- Разделение ответственности: ViewModel синхронизирует текущую задачу с актуальным wrapper-ом; XAML продолжает только биндинг selection и `AutoScrollToSelectedItem`.
- Повторное использование: использовать существующие `FindTaskWrapperViewModel`, `ExpandParentNodesForTask` и task identity comparison, без нового UI framework слоя.
- Тестируемость: regression test воспроизводит пользовательский flow через Avalonia.Headless.
- Консистентность: не ломать multi-selection, context menu selection restore, inline title edit и сохранение expansion state.
- Обратная совместимость: storage, task model, public API и automation-id не меняются.

## 5. Non-Goals (чего НЕ делаем)
- Не менять алгоритм fuzzy/search matching.
- Не менять структуру вкладок, XAML layout или визуальные стили.
- Не менять persisted state или формат файлов задач.
- Не расширять задачу на roadmap/graph selection.
- Не добавлять новые automation-id, если существующих достаточно.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `MainWindowViewModel` -> после search-driven rebuild `CurrentAllTasksItems` форсирует восстановление selection для `CurrentTaskItem` в активной `AllTasksMode`.
- `MainWindowViewModel.SelectCurrentTask` / helper -> сравнивает не только task identity, но и актуальность wrapper-а в текущей коллекции; при необходимости назначает wrapper из `CurrentAllTasksItems`.
- `MainWindowViewModel.ExpandParentNodesForTask` -> остается местом раскрытия parent chain и финальной переустановки `CurrentAllTasksItem`.
- `MainControlTreeCommandsUiTests` -> добавляет failing-first UI regression test для flow поиска, выбора и очистки.

### 6.2 Детальный дизайн
- В AllTasks DynamicData pipeline после `.Bind(out _currentItems)` использовать подписку на change-set как post-rebuild trigger для helper-а восстановления selection. Не привязывать production-логику только к raw `Search.SearchText`, потому что событие изменения текста может прийти до перестройки коллекции.
- Helper восстановления selection должен запускаться только когда `AllTasksMode == true` и `Search.SearchText` пустой или whitespace. Это сохраняет обычное поведение search projection во время активного поиска и закрывает именно сценарий очистки поиска.
- Refresh должен:
  - ничего не делать при `CurrentTaskItem == null`;
  - найти wrapper текущей задачи в актуальном `CurrentAllTasksItems`;
  - раскрыть parent chain в `AllTasksTree`;
  - назначить `CurrentAllTasksItem` на найденный актуальный wrapper, даже если старый wrapper ссылался на тот же `TaskItem`;
  - если wrapper не найден из-за других активных фильтров, не менять фильтры и не назначать stale wrapper;
  - при необходимости коротко сбросить `CurrentAllTasksItem` в `null` перед повторным назначением только если reproducing test покажет, что Avalonia не поднимает selection/scroll на замену wrapper-а. Такое решение должно остаться локальным к `CurrentAllTasksItem` и не менять `CurrentTaskItem`.
- Follow-up edge case: если карточка деталей закрыта, `CurrentTaskItem` может быть потерян после выбора результата поиска, хотя `CurrentAllTasksItem` еще хранит последний выбранный search wrapper. Для этого нужен transient fallback на последний выбранный AllTasks task, но только в search-clear restore path. Обычное явное очищение `CurrentTaskItem` вне search-clear не должно самовосстанавливать selection.
- Visual planning artifact для UI-facing изменений:
```text
AS-IS after clearing search:
All Tasks tree viewport
  [top of tree ...]
  selected task is outside viewport or wrapper is stale
  no visible highlight on expected task

TO-BE after clearing search:
All Tasks tree viewport
  Parent chain expanded
    > Parent
      > Child
        [selected] Task chosen from search
  TreeView.SelectedItem == CurrentAllTasksItem == actual wrapper in CurrentAllTasksItems
  AutoScrollToSelectedItem brings selected row into the visible viewport
```
- UI test video evidence: Не применимо как обязательный artifact, потому что существующий Avalonia.Headless test suite в репозитории не настроен на запись видео. Fallback evidence: deterministic UI assertions по `TreeView.SelectedItem`, `CurrentAllTasksItem`, `TreeViewItem.IsSelected`, parent expansion и intersection bounds selected `TreeViewItem` с viewport `AllTasksTree`.
- Границы сохранения поведения: multi-selection команды и context-menu normalize не меняются; исправление касается только восстановления single current selection после search clear.
- Обработка ошибок: если текущая задача не входит в текущие фильтры, selection остается `null`; это корректно для активных фильтров, скрывающих задачу.
- Производительность: один lookup по текущему дереву после throttle/search collection rebuild; масштаб сопоставим с существующим `FindTaskWrapperViewModel`.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Выбранная пользователем задача (`CurrentTaskItem`) является source of truth при очистке поиска в `AllTasksMode`.
- Если задача снова присутствует в полном `AllTasksTree`, она должна стать `CurrentAllTasksItem`.
- Родители выбранной задачи должны быть раскрыты до назначения финального selected wrapper, чтобы визуальный контейнер мог материализоваться.
- Если после очистки поиска задача все еще скрыта другими активными фильтрами, приложение не должно насильно менять фильтры.

## 8. Точки интеграции и триггеры
- `MainWindowViewModel.SelectCurrentTask()`
- `MainWindowViewModel.ExpandParentNodesForTask(TaskItemViewModel?)`
- DynamicData stream, который биндингуется в `CurrentAllTasksItems`; конкретная точка EXEC: `.Bind(out _currentItems).Subscribe(...)` в AllTasks roots pipeline
- `Search.SearchText` throttle / clear
- `AllTasksTree.SelectedItem="{Binding CurrentAllTasksItem}"` и `AutoScrollToSelectedItem="True"`

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Возможны только private helper/transient state changes в `MainWindowViewModel`; follow-up фикс допускает private `_lastSelectedAllTasksItem` без persistence.
- `TaskItemViewModel`, storage и migration не меняются.

## 10. Миграция / Rollout / Rollback
- Первый запуск не требует миграции.
- Rollout безопасен: исправление срабатывает только в UI session после пересборки дерева.
- Rollback: вернуть прежнее условие выбора и убрать search-clear selection refresh test.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В `All Tasks` пользователь выбирает найденную поиском вложенную задачу.
  - После очистки поиска `CurrentTaskItem` остается этой задачей.
  - После очистки поиска `CurrentAllTasksItem` указывает на актуальный wrapper из `CurrentAllTasksItems`, а не на stale wrapper из search projection.
  - Parent chain выбранной задачи раскрыт.
  - `AllTasksTree.SelectedItem` равен актуальному wrapper-у, соответствующий `TreeViewItem.IsSelected == true`.
  - Выбранная задача находится в визуальном дереве и selected `TreeViewItem` пересекает видимую область `AllTasksTree`; тест должен сделать viewport достаточно малым или данных достаточно много, чтобы проверка действительно покрывала автоскролл, а не только наличие item.
- Какие тесты добавить/изменить: новый Avalonia.Headless regression test в `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`.
- Characterization tests / contract checks: тест должен падать на текущем поведении до fix.
- Visual acceptance: соответствует текстовой схеме в 6.2; проверяется UI assertions вместо screenshot/video.
- UI video evidence: fallback по причине отсутствия recorder в текущем headless harness; next-best evidence - команда targeted UI test и assertion details по `SelectedItem`, `IsSelected`, expanded parent chain и selected item bounds относительно `AllTasksTree`.
- Базовые замеры performance: Не применимо, изменение не добавляет дорогой цикл сверх существующего lookup.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_ReselectsAndScrollsCurrentAllTasksItemWithClosedDetails"`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_RestoresExpansionState"`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/*"`
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`
- Если full solution build/test блокируется отсутствующими mobile/browser workloads, next-best validation: test project build + targeted UI test + full `src/Unlimotion.Test` run, с явным отчетом о блокере.
- Stop rules для test/retrieval/tool/validation loops: targeted reproducing test должен сначала подтвердить дефект; после fix targeted test должен пройти; full validation запускается до финала или фиксируется объективный blocker.

## 12. Риски и edge cases
- Если текущая задача скрыта не поиском, а emoji/completed/archive фильтром, force-selection не должен ломать фильтры.
- Если old wrapper и new wrapper указывают на один `TaskItem`, comparison только по task identity недостаточен; нужно учитывать актуальность wrapper-а в текущей коллекции.
- `SelectedItem` в `TreeView` с `SelectionMode=Multiple` может не синхронизировать `SelectedItems`; test должен проверять user-visible selected row, а не только VM property.
- Слишком ранний refresh до materialization visual item может не дать scroll; при необходимости refresh должен происходить после collection update / dispatcher jobs, но без таймеров в production-коде, если можно обойтись reactive collection event.
- Если карточка деталей закрыта, `CurrentTaskItem` может стать `null` между выбором search result и очисткой поиска; fallback должен использовать последний выбранный AllTasks item только для восстановления после search clear, иначе появится регрессия явного сброса текущей задачи.

## 13. План выполнения
1. Добавить Avalonia.Headless regression test, который создает вложенную задачу, выбирает ее в search view, очищает поиск и ожидает visible selected row в `AllTasksTree`.
2. Запустить targeted test и зафиксировать fail до fix.
3. Исправить refresh logic в `MainWindowViewModel` минимально, без изменений XAML selectors.
4. Повторить targeted UI test.
5. Запустить доступные build/full test команды.
6. Выполнить post-EXEC review-loop и обновить журнал спеки.

## 14. Открытые вопросы
Нет блокирующих.

## 15. Соответствие профилю
- Профиль: dotnet-desktop-client + ui-automation-testing
- Выполненные требования профиля на SPEC: выбран existing Avalonia.Headless UI suite; selectors остаются стабильными; visual planning artifact зафиксирован текстовой схемой; video evidence заменено fallback с объективной причиной.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-02-search-selection-restore.md` | рабочая спецификация и QUEST audit trail | обязательный SPEC-first gate |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | refresh актуального `CurrentAllTasksItem` после search-driven rebuild; transient fallback на последний выбранный AllTasks item для closed-details search-clear path | восстановить highlight/scroll выбранной задачи |
| `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` | UI regression test с закрытой карточкой деталей | зафиксировать пользовательский сценарий |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Очистка поиска после выбора задачи | `CurrentTaskItem` сохраняется, но tree selection может быть stale/невидимым | актуальный wrapper найден, родители раскрыты, row выделен и проскроллен |
| Очистка поиска при закрытой карточке деталей | выбранная в search карточка может потеряться, если `CurrentTaskItem == null` | search-clear restore использует последний выбранный AllTasks task как transient fallback |
| `CurrentAllTasksItem` | может указывать на wrapper из search projection или не обновляться | указывает на wrapper из текущего полного `CurrentAllTasksItems` |
| UI tests | покрыто раскрытие после поиска, но не selection/scroll | добавлен regression на selection + visible selected row |

## 18. Альтернативы и компромиссы
- Вариант: исправить только XAML через binding mode или attached behavior.
- Плюсы: меньше ViewModel-кода.
- Минусы: не решает stale wrapper identity и parent expansion; сложнее тестировать без зависимости от Avalonia internals.
- Почему выбранное решение лучше в контексте этой задачи: ViewModel уже владеет `CurrentTaskItem`, `CurrentAllTasksItem` и `ExpandParentNodesForTask`; исправление локально устраняет причину рассинхрона.

- Вариант: вызывать `SelectCurrentTask()` на каждое изменение `Search.SearchText`.
- Плюсы: просто.
- Минусы: может сработать до перестройки коллекции и сохранить stale wrapper.
- Почему выбранное решение лучше в контексте этой задачи: refresh должен быть привязан к актуальной коллекции или к post-rebuild моменту, чтобы выбранный wrapper реально существовал.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, state, rollback и visual artifact описаны. |
| C. Безопасность изменений | 11-13 | PASS | Acceptance, edge cases и план выполнения проверяемы. |
| D. Проверяемость | 14-16 | PASS | Открытых блокеров нет, файлы и команды указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, alternatives и review зафиксированы. |
| F. Соответствие профилю | 20 | PASS | UI automation, visual artifact и video fallback учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Описан один конкретный AllTasks search selection bug. |
| 2. Понимание текущего состояния | 5 | Зафиксированы stale wrapper и пропуск refresh по task identity. |
| 3. Конкретность целевого дизайна | 5 | Определен refresh актуального wrapper-а, parent expansion и selection contract. |
| 4. Безопасность (миграция, откат) | 5 | Нет persisted changes; rollback локальный. |
| 5. Тестируемость | 5 | Есть failing-first UI test и конкретные команды. |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов; scope малый. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-02-search-selection-restore.md`; instruction stack `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, local `AGENTS.override.md`; selected profile `dotnet-desktop-client + ui-automation-testing`; open questions: none; planned files: `MainWindowViewModel.cs`, `MainControlTreeCommandsUiTests.cs`.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: просмотрены центральные QUEST/testing/UI docs, локальный override, существующая expansion-state спека, релевантные участки `MainWindowViewModel`, `MainControl.axaml`, `MainControl.axaml.cs`, `MainControlTreeCommandsUiTests`, а также текущий `git status --short`.
  - Contract pass: spec содержит goal, AS-IS, Non-Goals, acceptance, commands, visual planning artifact, UI test evidence fallback, конкретную post-rebuild integration point и approval phrase.
  - Adversarial risk pass: проверены stale wrapper identity, hidden filters, multi-selection, timing/materialization, слишком мягкий scroll oracle, ранний raw-search trigger и video evidence gap.
  - Re-review after fixes / Fix and re-review: iteration 1 нашла две MEDIUM-находки; spec обновлена; iteration 2 повторно проверила измененные sections 1, 6, 8, 11 и подтвердила, что открытых BLOCKER/HIGH/MEDIUM не осталось.
  - Stop decision: PASS; оставшиеся замечания только LOW.
- Evidence inspected: `SelectCurrentTask`, `ExpandParentNodesForTask`, `AllTasksTree.SelectedItem`, `AutoScrollToSelectedItem`, AllTasks `.Bind(out _currentItems)` pipeline, helper patterns в existing headless tests.
- Depth checklist:
  - Scope drift / unrelated changes: scope ограничен `AllTasksTree` selection после search clear.
  - Acceptance criteria: проверяют VM state, актуальный wrapper, parent expansion, visual selected item и intersection bounds с видимой областью `AllTasksTree`.
  - Validation evidence: команды заданы; фактический запуск будет на EXEC.
  - Unsupported claims: claims основаны на прочитанном коде; performance claim ограничен lookup cost.
  - Regression / edge case: зафиксированы filters, multi-selection, timing/materialization и stale wrapper replacement.
  - Comments/docs/changelog: changelog не нужен для локального bugfix; комментарии не планируются.
  - Hidden contract change: public API и automation-id не меняются.
  - Manual-review challenge: вероятная находка reviewer была бы отсутствие video evidence; spec содержит объективный fallback.
- No-findings justification: после iteration 2 нет открытых замечаний выше LOW; spec содержит проверяемый scroll oracle, конкретную DynamicData integration point и обязательные profile artifacts.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | acceptance | Scroll был сформулирован как `при возможности`, что могло позволить test пройти без проверки пользовательского ожидания увидеть выбранную задачу на экране | Сделать viewport/scroll oracle обязательным через selected `TreeViewItem` intersection bounds с `AllTasksTree` | fixed |
| MEDIUM | design | Trigger был описан как изменение `Search.SearchText` или `CurrentAllTasksItems`, что оставляло риск запуска refresh до rebuild коллекции | Зафиксировать post-rebuild trigger в AllTasks DynamicData pipeline после `.Bind(out _currentItems).Subscribe(...)` и условие empty search | fixed |
| LOW | evidence | Нет видео из UI test run, потому что текущий Avalonia.Headless harness не настроен на recorder | Использовать deterministic UI assertions как fallback evidence | accepted-risk |

- Fixed before continuing: усилен outcome/acceptance по видимой области; уточнен post-rebuild trigger и empty-search guard; обновлен UI evidence fallback.
- Checks rerun: SPEC linter/rubric manual pass после правок; iteration 2 post-SPEC re-review.
- Needs human: approval-gate.
- Residual risks / follow-ups: full solution validation может быть заблокирован отсутствующими workloads; это будет проверено на EXEC.

### Post-EXEC Review
- Статус: PASS с residual LOW validation risks
- Scope reviewed: approved spec `specs/2026-06-02-search-selection-restore.md`; current `git status --short` = `M src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `M src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `?? specs/2026-06-02-search-selection-restore.md`; current `git diff --stat` = 2 tracked files, 185 insertions, 9 deletions; relevant diffs for `MainWindowViewModel.cs` and `MainControlTreeCommandsUiTests.cs`; untracked spec content; targeted UI test output; docs/changelog impact = none.
- Decision: изменение соответствует утвержденной spec; можно финализировать без дополнительных code changes.
- Review passes:
  - Scope/Evidence pass: просмотрены approved spec, `review-loops.md`, local UI testing override, current `git status --short`, current `git diff --stat`, relevant production/test diffs and targeted UI validation evidence; изменены только ожидаемые ViewModel, UI regression test и spec; XAML, storage, task model и automation-id не менялись.
  - Contract pass: helper восстанавливает актуальный wrapper из `CurrentAllTasksItems`; refresh запускается после `.Bind(out _currentItems)` только на переход active search -> empty search в `AllTasksMode`; `SelectCurrentTask()` использует тот же helper.
  - Adversarial risk pass: проверены stale wrapper identity, parent materialization, visual selected row, scroll viewport intersection, hidden filters fallback и старый expansion-state сценарий.
  - Re-review after fixes / Fix and re-review: initial targeted test сначала воспроизвел дефект; после implementation потребовался scheduled restore через `RxSchedulers.MainThreadScheduler`, потому что Avalonia `TreeView` очищал selection после collection rebuild; повторный targeted test прошел. Follow-up EXEC review нашел LOW gap в самом review artifact: scope не фиксировал `git status --short` и `git diff --stat` достаточно явно; spec обновлена, fresh targeted test rerun прошел.
  - Stop decision: PASS; remaining issues classified as LOW/unrelated validation environment risks.
- Evidence inspected: `instructions/governance/review-loops.md`, approved spec, `git status --short`, `git diff --stat`, diff `MainWindowViewModel.cs`, diff `MainControlTreeCommandsUiTests.cs`, targeted test reports, solution build/test output.
- Depth checklist:
  - Scope drift / unrelated changes: none observed in code diff; generated test reports remain under ignored `bin/TestResults`.
  - Acceptance criteria: targeted regression asserts `CurrentTaskItem`, fresh `CurrentAllTasksItem`, `TreeView.SelectedItem`, `TreeViewItem.IsSelected`, expanded parent and visible bounds intersection.
  - Validation evidence: targeted UI tests pass; solution build passes; full solution test has an existing cross-project/headless instability documented below.
  - Unsupported claims: scroll claim is backed by `TreeViewItem` bounds intersection with `AllTasksTree`.
  - Regression / edge case: old expansion restore test still passes; hidden-filter behavior remains no force-filter-change.
  - Comments/docs/changelog: no changelog needed; no extra code comments added.
  - Hidden contract change: no public API, storage, XAML selector or automation-id changes.
  - Manual-review challenge: video evidence unavailable in current headless harness; deterministic UI assertions are the fallback evidence.
- No-findings justification: no open BLOCKER/HIGH/MEDIUM findings after EXEC review; remaining validation anomalies are unrelated or environment/test-runner behavior and are reported explicitly.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | `dotnet test src/Unlimotion.sln --no-build` в параллельном solution run падал в `tests/Unlimotion.UiTests.Headless` с Avalonia cross-thread exception; тот же Headless-проект отдельно проходит 25/25 | Сообщить как residual environment/test-runner risk; не блокирует targeted fix | accepted-risk |
| LOW | validation | `dotnet test src/Unlimotion.sln --no-build -m:1` не применим к Microsoft Testing Platform: `-m:1` пробрасывается в host как unknown `--m` | Использовать отдельные проектные прогоны вместо этого флага | accepted-risk |
| LOW | validation | Один обычный full run `src/Unlimotion.Test` дал flaky-сбой `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`; одиночный rerun этого теста прошел | Сообщить как unrelated flake; targeted search tests проходят | accepted-risk |
| LOW | validation | `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build --maximum-parallel-tests 1` не завершился за 15 минут | Остановить зависший процесс; не использовать как gate для этого small UI fix | accepted-risk |
| LOW | review artifact | Первичный Post-EXEC блок не фиксировал current `git status --short` и `git diff --stat` в `Scope reviewed` достаточно явно для strict `review-loops.md` audit | Обновить `Scope reviewed`/`Evidence inspected`, добавить finding и fresh targeted validation evidence | fixed |

- Fixed before final report: helper вынесен в `RestoreCurrentAllTasksSelection`; AllTasks root pipeline восстанавливает selection после search-clear rebuild; добавлен scheduled restore на UI scheduler; regression test стабилизирован на существующей parent/child fixture relation.
- Checks rerun:
  - `dotnet restore src/Unlimotion.sln -v minimal` -> PASS, с существующим `NU1608` warning для Android LibGit2Sharp NativeBinaries.
  - `dotnet build src/Unlimotion.sln --no-restore` -> PASS, 85 warnings, 0 errors.
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore` -> PASS.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_ReselectsAndScrollsCurrentAllTasksItem"` -> PASS, 1/1.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_RestoresExpansionState"` -> PASS, 7/7.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build --treenode-filter "/*/*/MainWindowViewModelTests/PasteTaskOutline_CreatesNestedTasksUnderCurrentTask"` -> PASS, 1/1 after one unrelated full-run failure.
  - `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj --no-build` -> PASS, 25/25.
  - Earlier `dotnet test src/Unlimotion.sln --no-build` -> partial FAIL: `src/Unlimotion.Test` passed 423/423 and FlaUI passed, Headless crashed only in solution-level run with cross-thread exception.
  - Follow-up EXEC review rerun: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_ReselectsAndScrollsCurrentAllTasksItem"` -> PASS, 1/1.
- Validation evidence:
  - Reproducing UI test before fix: after precondition correction, failed on `restoredSelectionVisible == false`, matching the reported bug.
  - Regression UI test after fix: selected task parent expands, full-tree wrapper differs from search wrapper, `CurrentAllTasksItem` and `AllTasksTree.SelectedItem` reference the restored wrapper, selected `TreeViewItem.IsSelected == true`, and item intersects visible `AllTasksTree` bounds.
  - HTML reports: `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/Unlimotion.Test-windows-net10.0-report.html`, `tests/Unlimotion.UiTests.Headless/bin/Debug/net10.0/TestResults/Unlimotion.UiTests.Headless-windows-net10.0-report.html`.
- Unrelated changes: none detected.
- Needs human: no further decision needed.
- Residual risks / follow-ups: investigate suite-level UI parallelism/cross-thread instability separately if full solution test must become a reliable gate.

### Follow-up Post-EXEC Review: closed details selection loss
- Статус: PASS с residual LOW full-suite risks
- Scope reviewed: follow-up user report "Если карточка закрыта, то выбранная карточка после закрытия поиска теряется"; approved spec addendum; current `git status --short`; current `git diff --stat`; relevant diffs for `MainWindowViewModel.cs`, `MainControlTreeCommandsUiTests.cs`, this spec; targeted and class-level TUnit outputs; full `Unlimotion.Test` outputs.
- Decision: follow-up defect fixed in the same PR branch; можно коммитить и пушить update PR.
- Review passes:
  - Scope/Evidence pass: проверены только expected files `MainWindowViewModel.cs`, `MainControlTreeCommandsUiTests.cs`, `specs/2026-06-02-search-selection-restore.md`; XAML diff отсутствует по content, только line-ending warnings/status noise.
  - Contract pass: `CurrentAllTasksItem` subscription теперь запоминает последний выбранный AllTasks task; search-clear restore может использовать этот fallback, если `CurrentTaskItem == null`; обычный `RestoreCurrentAllTasksSelection()` без fallback не восстанавливает явно очищенный current task.
  - Adversarial risk pass: проверены риски самовосстановления selection вне search-clear, stale wrapper из search projection, closed details path, expansion-state regression и full-suite order/pollution.
  - Re-review after fixes / Fix and re-review: после добавления fallback targeted closed-details test прошел; старый expansion test прошел; весь `MainControlTreeCommandsUiTests` класс прошел; full `Unlimotion.Test` rerun показал failures вне изолированного targeted/class прогона и зафиксирован как LOW residual suite risk.
  - Stop decision: PASS; нет BLOCKER/HIGH/MEDIUM findings по follow-up change.
- Evidence inspected: `RestoreCurrentAllTasksSelection(bool useLastSelectedFallback = false)`, `CurrentAllTasksItem` subscription, AllTasks search-clear DynamicData branch, closed-details UI regression test, expansion-state test, class-level test run, full `Unlimotion.Test` output.
- Depth checklist:
  - Scope drift / unrelated changes: no content diff outside ViewModel, UI test and spec; `MainControl.axaml`/`.cs` show status noise but no diff/stat.
  - Acceptance criteria: closed-details regression asserts `DetailsAreOpen == false`, selected search wrapper, `CurrentTaskItem == null` pre-clear, restored full-tree wrapper, parent expanded, selected row visible.
  - Validation evidence: targeted and class-level UI tests pass; build passes; full project suite attempted twice and reported below.
  - Unsupported claims: fallback scope is backed by call sites: only search-clear passes `useLastSelectedFallback: true`.
  - Regression / edge case: explicit task clearing outside search-clear remains non-restoring; expansion-state tests pass in isolated reruns.
  - Comments/docs/changelog: no code comments/changelog required; spec updated with addendum.
  - Hidden contract change: no persisted state, public API, XAML selector or automation-id changes.
  - Manual-review challenge: likely reviewer concern is whether fallback reselects stale tasks outside search clear; helper default and call-site split address it.
- No-findings justification: targeted defect is covered by a deterministic UI test and current diff is limited to the intended ViewModel/test/spec surface.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build` timed out after 5 minutes and reported unrelated failures in `BackupViaGitServiceTests.GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows`, `SettingsViewModelTests.SwitchRemoteConnectionTypeCommand_UpdatesSelectedRemoteFromServiceResult`, plus order-sensitive `TreeSearch_ClearSearch_RestoresExpansionState` cases that pass when rerun separately | Report as residual full-suite blocker; use targeted/class UI evidence for this PR update | accepted-risk |
| LOW | validation | `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --maximum-parallel-tests 1` completed with 420/423 passed and the same ACL failure plus order-sensitive tree cases | Report as residual suite/order issue; do not broaden this PR to unrelated test isolation | accepted-risk |
| LOW | evidence | No UI video evidence because Avalonia.Headless harness does not provide recorder artifacts | Use deterministic UI assertions and HTML test report path as fallback | accepted-risk |

- Fixed before final report: added `_lastSelectedAllTasksItem` transient state; search-clear restore passes `useLastSelectedFallback: true`; closed-details regression test now simulates lost `CurrentTaskItem` before clearing search.
- Checks rerun:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_ReselectsAndScrollsCurrentAllTasksItemWithClosedDetails"` -> PASS, 1/1.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_RestoresExpansionState"` -> PASS, 7/7.
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore` -> PASS, 4 line-ending warnings, 0 errors.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/*"` -> PASS, 42/42.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_ReselectsAndScrollsCurrentAllTasksItemWithClosedDetails"` -> PASS after full-suite failure rerun, 1/1.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_RestoresExpansionState"` -> PASS after full-suite failure rerun, 7/7.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build` -> FAIL/TIMEOUT after 5 minutes; see LOW finding.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --maximum-parallel-tests 1` -> FAIL, 420/423 passed; see LOW finding.
- Validation evidence: `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/Unlimotion.Test-windows-net10.0-report.html`.
- Unrelated changes: none intended; XAML files show status noise without content diff.
- Needs human: no decision needed for this follow-up.
- Residual risks / follow-ups: separate investigation needed for full `Unlimotion.Test` suite order/environment failures if it must be a reliable delivery gate.

### Follow-up Post-EXEC Review: first AllTasks search after rebase
- Статус: PASS
- Scope reviewed: user report "теперь поиск во всех задачах вообще не работает"; approved spec and current follow-up scope; current `git diff --stat`; relevant diffs for `MainWindowViewModel.cs` and `MainControlTreeCommandsUiTests.cs`; targeted UI test evidence; full `Unlimotion.Test` run.
- Decision: regression fixed in the same PR branch; можно коммитить и пушить update PR.
- Review passes:
  - Scope/Evidence pass: inspected AllTasks search predicate chain, AllTasks DynamicData bind/sort chain, search-clear restore path, new UI regression test and validation outputs.
  - Contract pass: first real search event is no longer dropped after the initial empty value; AllTasks collection uses `TreatMovesAsRemoveAdd()` again to avoid Avalonia `TreeView` out-of-range move handling; search-clear restore repeats after UI selection reset.
  - Adversarial risk pass: checked first search from visible `SearchEditor`, closed-details search clear, expansion-state restore across all task trees, and full project test run.
  - Re-review after fixes / Fix and re-review: initial reproduction failed because search text reached `Search.SearchText` but parent stayed visible; after search predicate fix, `SortAndBind` exposed a TreeView collection-move exception; after restoring `TreatMovesAsRemoveAdd()` and delayed restore, all targeted tests passed.
  - Stop decision: PASS; no BLOCKER/HIGH/MEDIUM findings remain.
- Evidence inspected: `MainWindowViewModel.cs` `searchTopFilter`, AllTasks root pipeline, `RestoreCurrentAllTasksSelectionAfterSearchClear`, `MainControlTreeCommandsUiTests.TreeSearch_AllTasksSearchEditor_FiltersVisibleTree`, targeted and full test outputs.
- Depth checklist:
  - Scope drift / unrelated changes: expected ViewModel/test/spec surface only; accidental `SearchBar.axaml` probe removed before commit.
  - Acceptance criteria: first AllTasks search filters out a non-matching parent and keeps the matching nested task; clearing search restores selected wrapper/row with closed details; expansion state still restores.
  - Validation evidence: build passes; targeted UI tests pass; full `src/Unlimotion.Test` run passes 424/424.
  - Unsupported claims: user-visible search/filter and restore claims are backed by Avalonia.Headless assertions.
  - Regression / edge case: `TreatMovesAsRemoveAdd()` intentionally keeps deprecated `Sort` in this one AllTasks UI pipeline because `SortAndBind` caused an Avalonia `TreeView` insert index exception under search filtering.
  - Comments/docs/changelog: no code comments or changelog needed; spec updated with follow-up audit.
  - Hidden contract change: no public API, persisted state, XAML selector or automation-id changes.
  - Manual-review challenge: reviewer may ask why not `SortAndBind`; targeted run reproduced the TreeView collection exception with `SortAndBind`, while explicit sort/move-as-remove-add passes.
- No-findings justification: current regression is covered by deterministic UI tests and the full test project passes on the final diff.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | implementation | AllTasks root pipeline uses deprecated `Sort` API to retain `TreatMovesAsRemoveAdd()` semantics | Keep as scoped compatibility workaround; document in review evidence | accepted-risk |
| LOW | evidence | No UI video evidence because Avalonia.Headless harness does not provide recorder artifacts | Use deterministic UI assertions and HTML test report path as fallback | accepted-risk |

- Fixed before final report: removed skipped first search event in `searchTopFilter`; restored AllTasks `Sort(...).TreatMovesAsRemoveAdd().Bind(...)`; added delayed search-clear restore; added first-search UI regression.
- Checks rerun:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore` -> PASS, warnings only.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_AllTasksSearchEditor_FiltersVisibleTree"` -> PASS, 1/1.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_ReselectsAndScrollsCurrentAllTasksItemWithClosedDetails"` -> PASS, 1/1.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_ClearSearch_RestoresExpansionState"` -> PASS, 7/7.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build` -> PASS, 424/424.
- Validation evidence: `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/Unlimotion.Test-windows-net10.0-report.html`.
- Unrelated changes: none.
- Needs human: no decision needed.
- Residual risks / follow-ups: none for this PR scope.

## Approval
Подтверждено пользователем: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction stack и первичный анализ | 0.86 | Нет | Создать рабочую спецификацию | Нет | Нет | Central QUEST применим к UI bugfix; локальный override требует UI test | `AGENTS.md`, central docs, `AGENTS.override.md`, `MainWindowViewModel.cs`, `MainControl.axaml`, `MainControl.axaml.cs`, `MainControlTreeCommandsUiTests.cs` |
| SPEC | Spec drafting и post-SPEC review | 0.9 | Требуется approval пользователя | Запросить `Спеку подтверждаю` | Да | Да: будет запрошено подтверждение | Scope ограничен восстановлением актуального selected wrapper-а после search clear в AllTasks | `specs/2026-06-02-search-selection-restore.md` |
| SPEC | Iterative spec review | 0.93 | Требуется approval пользователя | Запросить `Спеку подтверждаю` | Да | Да: пользователь запросил iterative spec review до остатка только LOW | Исправлены две MEDIUM-находки: слабый scroll oracle и неконкретный DynamicData trigger; после re-review открыты только LOW | `specs/2026-06-02-search-selection-restore.md` |
| EXEC | Approval received | 1.0 | Нет | Добавить reproducing UI test | Нет | Да: `Спеку подтверждаю` | Approval-gate снят, можно менять код и тесты в границах спеки | `specs/2026-06-02-search-selection-restore.md` |
| EXEC | Reproducing UI test | 0.9 | Нет | Исправить `MainWindowViewModel` | Нет | Нет | Добавлен targeted UI test; после корректировки precondition он падает на `restoredSelectionVisible == false`, то есть воспроизводит заявленный дефект | `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` |
| EXEC | Implementation | 0.9 | Нет | Повторить targeted UI validation | Нет | Нет | AllTasks pipeline теперь восстанавливает selection после post-rebuild перехода active search -> empty search; `SelectCurrentTask` использует тот же refresh helper | `src/Unlimotion.ViewModel/MainWindowViewModel.cs` |
| EXEC | Validation | 0.88 | Нет по targeted flow; full solution имеет unrelated runner instability | Обновить post-EXEC review | Нет | Нет | Новый UI regression test и старый expansion-state test прошли; build прошел; external validation anomalies зафиксированы как LOW | `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, test reports |
| EXEC | Post-EXEC review | 0.93 | Нет | Финальная проверка diff/status | Нет | Нет | Scope соответствует approved spec; open findings выше LOW отсутствуют | `specs/2026-06-02-search-selection-restore.md` |
| EXEC | Follow-up EXEC review | 0.94 | Нет | Финальный отчет пользователю | Нет | Нет | По запросу пользователя повторно сверен full post-EXEC review-loop с `review-loops.md`; найден и исправлен LOW gap в фиксации `git status --short`/`git diff --stat`; fresh targeted UI test прошел | `specs/2026-06-02-search-selection-restore.md`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs` |
| EXEC | Closed-details follow-up fix | 0.91 | Нет по targeted flow; full `Unlimotion.Test` suite имеет residual failures вне isolated UI surface | Коммит и push PR update | Нет | Да: пользователь сообщил новый edge case после PR | Добавлен fallback на последний выбранный AllTasks item только для search-clear restore, чтобы закрытая карточка деталей не теряла выбранную задачу; targeted/class UI tests прошли | `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `specs/2026-06-02-search-selection-restore.md` |
| EXEC | First-search follow-up fix | 0.94 | Нет | Коммит и push PR update | Нет | Да: пользователь сообщил регрессию поиска после ребейза | Исправлен dropped first search event, возвращена безопасная AllTasks sort/bind цепочка для TreeView и добавлен UI regression на первый ввод поиска; targeted/full tests прошли | `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `specs/2026-06-02-search-selection-restore.md` |
