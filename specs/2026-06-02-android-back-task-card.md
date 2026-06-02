# Android Back Gesture Toggles Task Card

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая detached worktree ветка, уточняется на EXEC
- Ограничения: до утверждения менять только эту спецификацию; UI-поведение покрыть тестами; не менять публичный API приложения без необходимости
- Связанные ссылки: Не применимо

Если секция не применима, указано `Не применимо` с причиной.

## 1. Overview / Цель
Жест назад на Android должен переключать карточку выбранной задачи: если карточка открыта, закрыть ее; если карточка закрыта и задача выбрана, открыть ее.

Outcome contract:
- Success means: системный Android back не закрывает приложение, когда есть выбранная задача; вместо этого меняет `DetailsAreOpen` и видимость карточки задачи.
- Итоговый артефакт / output: правка Android back integration, общий тестируемый ViewModel-контракт и regression/UI тесты.
- Stop rules: остановиться после targeted UI/headless тестов, `dotnet build`, полного доступного test-run или явного отчета о невозможности полного прогона.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion.Android/MainActivity.cs` на 2026-06-02 не содержит обработчика back gesture / `OnBackPressed`.
- `src/Unlimotion/Views/MainControl.axaml` использует `SplitView.IsPaneOpen="{Binding DetailsAreOpen}"`; панель задачи справа, `DetailsPaneToggleButton` меняет это состояние через binding.
- Карточка задачи внутри панели показывается при `CurrentTaskItem != null`; поле заголовка имеет automation id `CurrentTaskTitleTextBox`.
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs` хранит `CurrentTaskItem` и `DetailsAreOpen`; при закрытии деталей закрывает relation editor.
- `src/Unlimotion/App.axaml.cs` держит singleton `MainWindowViewModel` для desktop/single-view startup, но сейчас не предоставляет публичный back-navigation контракт.
- AppAutomation Headless/FlaUI уже используют `MainWindowPage` с `DetailsPaneToggleButton` и `CurrentTaskTitleTextBox`.

## 3. Проблема
Android system back gesture не связан с состоянием карточки задачи, поэтому пользователь на телефоне не получает ожидаемое открытие/закрытие карточки через системную навигацию.

## 4. Цели дизайна
- Разделение ответственности: Android слой только получает system back; решение о том, можно ли обработать back, живет в общем UI/ViewModel-контракте.
- Повторное использование: логика переключения не дублирует XAML binding кнопки.
- Тестируемость: основной контракт покрывается unit test, UI state plumbing покрывается существующим AppAutomation Headless сценарием.
- Консистентность: сохраняется текущий `DetailsAreOpen` и `CurrentTaskItem` model.
- Обратная совместимость: если задачи нет или app/view model не готова, Android back остается default behavior.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем layout карточки задачи, `SplitView` размеры, tabs или визуальный стиль.
- Не добавляем новый navigation stack.
- Не меняем поведение desktop keyboard shortcuts.
- Не меняем storage, sync, task model или persisted settings.
- Не внедряем новый UI automation framework и не коммитим video artifacts.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `MainWindowViewModel` -> метод вида `TryHandleTaskCardBackGesture()` / `TryToggleTaskCardFromBackGesture()`: если `CurrentTaskItem == null`, вернуть `false`; иначе инвертировать `DetailsAreOpen` и вернуть `true`.
- `App.axaml.cs` -> статический facade для platform layer, который безопасно вызывает текущий `MainWindowViewModel` на UI thread.
- `MainActivity.cs` -> Android back compatibility layer без новых package dependencies:
  - для API 33+ зарегистрировать guarded callback через `OnBackInvokedDispatcher` в `OnCreate` и снять регистрацию в `OnDestroy`, если API/SDK symbols доступны в `net10.0-android`;
  - для legacy path оставить `OnBackPressed()` override;
  - оба пути вызывают один helper, который при `handled=true` не выполняет default back, а при `handled=false` вызывает platform default.
- `MainWindowScenariosBase.cs` -> UI regression на открытие/закрытие task card state через существующие элементы.
- `MainWindowViewModelTests.cs` -> unit regression для back-contract: open -> close, closed -> open, no current task -> false/no mutation.

### 6.2 Детальный дизайн
- Поток данных:
  1. Android system back gesture приходит в `MainActivity`.
  2. `MainActivity` вызывает общий app-level handler.
  3. Handler обращается к текущему `MainWindowViewModel`.
  4. ViewModel переключает `DetailsAreOpen`, только если есть `CurrentTaskItem`.
  5. Avalonia binding обновляет `SplitView.IsPaneOpen` и видимость `CurrentTaskDetailsScrollViewer`.
- Контракты / API: новый метод ViewModel возвращает `bool handled`; `true` означает, что Android default back нельзя выполнять. Предпочтительно сделать метод public на `MainWindowViewModel`, как остальные тестируемые UI commands/state methods в этом классе; internal допустим только если в проекте уже есть подходящий `InternalsVisibleTo` или он не нужен тестам.
- Output contract / evidence rules: изменение подтверждается assertion-тестами по состоянию ViewModel и headless UI-потоком карточки.
- Visual planning artifact для UI-facing изменений:

```text
Phone/single view, selected task exists

State A: task list + task card open
System Back -> DetailsAreOpen=false -> task card closed, task list remains

State B: task list + selected task + task card closed
System Back -> DetailsAreOpen=true -> task card open with the same selected task

State C: no selected task / app not ready
System Back -> not handled -> Android default behavior
```

- UI test video evidence: fallback. Локальный AppAutomation Headless/TUnit workflow в текущем репозитории дает assertion/log evidence; безопасного автоматического video artifact path в найденном runner pattern нет. В EXEC зафиксировать команды и next-best evidence; если обнаружится штатная запись видео, использовать ее.
- Границы сохранения поведения: кнопка `DetailsPaneToggleButton` продолжает работать; relation editor продолжает закрываться при `DetailsAreOpen=false`.
- Обработка ошибок: если app/view model еще не создана, handler возвращает `false`; Android выполняет default back. Android callback registration/unregistration не должен бросать на unsupported API и должен быть guarded по версии ОС.
- Производительность: одно чтение singleton ViewModel и переключение bool; UI thread не блокируется длительной работой.

## 7. Бизнес-правила / Алгоритмы (если есть)
Правило обработки back:

| Условие | Действие | handled |
| --- | --- | --- |
| `CurrentTaskItem != null && DetailsAreOpen == true` | `DetailsAreOpen = false` | `true` |
| `CurrentTaskItem != null && DetailsAreOpen == false` | `DetailsAreOpen = true` | `true` |
| `CurrentTaskItem == null` | ничего не менять | `false` |
| ViewModel/app не готова | ничего не менять | `false` |

## 8. Точки интеграции и триггеры
- Android system back / gesture navigation -> `MainActivity`.
- App-level facade -> текущий `MainWindowViewModel`.
- `DetailsAreOpen` binding -> `SplitView.IsPaneOpen` и `CurrentTaskDetailsScrollViewer.IsVisible`.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- `DetailsAreOpen` остается runtime UI state.
- `CurrentTaskItem` не меняется при back gesture.

## 10. Миграция / Rollout / Rollback
- Миграция данных: Не применимо, persisted state не меняется.
- Rollout: изменение активно только на Android back gesture; desktop UI продолжает работать прежними путями.
- Rollback: удалить Android override/facade/ViewModel method и связанные тесты; behavior вернется к platform default.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - При выбранной задаче и открытой карточке Android back закрывает карточку и не выходит из приложения.
  - При выбранной задаче и закрытой карточке Android back открывает карточку с той же задачей.
  - Без выбранной задачи Android back не считается обработанным.
  - `DetailsPaneToggleButton` и существующая карточка задачи не регрессируют.
- Какие тесты добавить/изменить:
  - Unit tests в `src/Unlimotion.Test/MainWindowViewModelTests.cs` для нового back-contract.
  - UI/AppAutomation Headless scenario в `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`, проверяющий open/closed state карточки через `CurrentTaskDetailsScrollViewer`.
  - Минимально расширить `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` selector-ом `CurrentTaskDetailsScrollViewer`, если существующая page object не дает стабильной проверки видимости панели.
- Characterization tests / contract checks:
  - Сначала добавить failing unit test для отсутствующего back-contract.
  - Затем добавить/обновить UI test по карточке.
- Visual acceptance:
  - State A/B/C из текстового storyboard выше.
  - Проверить через automation ids `CurrentTaskDetailsScrollViewer`, `CurrentTaskTitleTextBox`, `DetailsPaneToggleButton`.
- UI video evidence:
  - Fallback: если runner не пишет видео, указать это в итоговом EXEC отчете и приложить команды/логи targeted UI test.
  - Команды-кандидаты:
    - `dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*TaskCard*"`
    - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/*TaskCardBack*"`
    - `dotnet build src/Unlimotion.sln`
    - `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -f net10.0-android` если локальный Android SDK доступен; если нет, явно указать средовую причину и next-best compile evidence.
    - полный доступный TUnit-run по executable test projects после targeted проверок: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj`, `dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`; FlaUI/Android build добавить, если среда позволяет.
- Базовые замеры performance: Не применимо, изменение O(1) UI state.
- Stop rules для validation loops:
  - Если targeted test падает, исправить и повторить targeted.
  - Если full test-run невозможен из-за внешней среды/долгого runner failure вне scope, зафиксировать точную причину и next-best проверку.

## 12. Риски и edge cases
- Android API может считать `OnBackPressed` obsolete; spec требует legacy override плюс guarded API 33+ callback where compile-supported, без добавления AndroidX Activity dependency только ради back handling.
- Back gesture может прийти до инициализации Avalonia view model; handler должен вернуть `false`.
- Если relation editor открыт внутри карточки, закрытие карточки уже закрывает editor через существующую подписку на `DetailsAreOpen=false`.
- UI test не сможет напрямую сгенерировать Android gesture в Headless; unit test должен покрыть platform-independent contract, UI test - состояние карточки, Android build - compile compatibility callback layer.

## 13. План выполнения
1. Добавить failing unit test на back-contract в `MainWindowViewModelTests`.
2. Добавить ViewModel метод и app-level facade.
3. Подключить Android `MainActivity` legacy + API 33+ guarded back callbacks к facade.
4. Добавить/обновить AppAutomation Headless UI test для opening/closing card state, используя `CurrentTaskDetailsScrollViewer` как основной state selector.
5. Запустить targeted tests, затем solution build, Android build при доступном SDK и full доступный TUnit-run.
6. Выполнить post-EXEC review-loop и зафиксировать validation evidence.

## 14. Открытые вопросы
Нет блокирующих вопросов. Продуктовый выбор трактуется буквально: back gesture должен именно переключать карточку open/close, а не только закрывать ее.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`.
- Выполненные требования профиля:
  - UI-thread не должен блокироваться.
  - UI flow покрывается тестами.
  - Стабильные automation ids используются существующие; новые добавлять только если без них нельзя надежно проверить state.
  - Перед завершением EXEC запланированы `dotnet build` и тестовый прогон.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Добавить testable back-card toggle method | Центральная логика состояния карточки |
| `src/Unlimotion/App.axaml.cs` | Добавить app-level facade для platform back handler | Дать Android слою безопасную точку входа |
| `src/Unlimotion.Android/MainActivity.cs` | Перехватить Android back и вызвать facade | Связать жест телефона с карточкой |
| `src/Unlimotion.Test/MainWindowViewModelTests.cs` | Unit regression tests | Проверить алгоритм без Android runtime |
| `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` | Headless UI scenario | Подтвердить UI state карточки |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Добавить selector `CurrentTaskDetailsScrollViewer`, если page object сейчас не содержит его | Стабильно проверять open/closed state панели |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Android back gesture | Не управляет карточкой задачи | Переключает `DetailsAreOpen` при выбранной задаче |
| Открытая карточка | Back может уйти в default platform behavior | Back закрывает карточку |
| Закрытая карточка | Back не открывает карточку | Back открывает карточку той же задачи |
| Нет выбранной задачи | Не определено в контексте карточки | Handler возвращает `false`, default behavior сохраняется |

## 18. Альтернативы и компромиссы
- Вариант: дергать `SplitView` напрямую из Android.
  - Плюсы: меньше кода.
  - Минусы: плохо тестируется, нарушает разделение platform/UI state.
  - Почему не выбран: ViewModel уже владеет `DetailsAreOpen`.
- Вариант: back только закрывает карточку.
  - Плюсы: ближе к типовой Android навигации.
  - Минусы: противоречит формулировке "открывать/закрывать".
  - Почему не выбран: задача явно требует toggle.
- Вариант: ввести полноценный navigation stack.
  - Плюсы: расширяемо.
  - Минусы: больше scope и риск.
  - Почему не выбран: текущая задача точечная.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, integration points, state, rollback и edge cases описаны. |
| C. Безопасность изменений | 11-13 | PASS | Нет persisted migration; rollback и границы поведения указаны. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, TUnit-compatible targeted/full команды, Android build check и UI fallback evidence указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План есть, открытых блокеров нет, альтернативы рассмотрены. |
| F. Соответствие профилю | 20 | PASS | UI tests, dotnet build/test и selectors учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Toggle behavior и Non-Goals конкретны. |
| 2. Понимание текущего состояния | 5 | Указаны Android, App, ViewModel, XAML и test harness точки. |
| 3. Конкретность целевого дизайна | 5 | Метод, facade, Android handler и test split описаны. |
| 4. Безопасность (миграция, откат) | 5 | Persisted данных нет; default behavior сохраняется при no-task/not-ready. |
| 5. Тестируемость | 5 | Есть TUnit-compatible unit + UI regression план, Android build check и команды. |
| 6. Готовность к автономной реализации | 5 | Нет открытых блокеров; решение малое и локализованное. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-02-android-back-task-card.md`, instruction stack (`model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, local `AGENTS.override.md`), selected profiles, open questions, planned changed files
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: просмотрены `MainActivity.cs`, `MainControl.axaml`, `MainWindowViewModel.cs`, `App.axaml.cs`, `MainWindowPage.cs`, `MainWindowScenariosBase.cs`, relevant search results.
  - Contract pass: spec сохраняет SPEC-only mutation boundary, покрывает UI test requirement и не расширяет scope за пределы Android back/card state.
  - Adversarial risk pass: проверены no-task/not-ready cases, obsolete Android callback risk, relation editor side effect, TUnit runner syntax, selector stability и отсутствие video runner evidence.
  - Re-review after fixes / Fix and re-review: исправлены non-LOW findings по TUnit runner syntax, Android callback specificity и UI selector specificity; повторно сверены sections 6, 11, 12, 13, 16, linter/rubric rows.
  - Stop decision: PASS, non-LOW findings закрыты; остаются только LOW residual risks/fallbacks.
- Evidence inspected:
  - `src/Unlimotion.Android/MainActivity.cs`: отсутствует back handler.
  - `src/Unlimotion/Views/MainControl.axaml`: `SplitView.IsPaneOpen` привязан к `DetailsAreOpen`, есть `DetailsPaneToggleButton`, `CurrentTaskDetailsScrollViewer`, `CurrentTaskTitleTextBox`.
  - `src/Unlimotion.ViewModel/MainWindowViewModel.cs`: есть `CurrentTaskItem`, `DetailsAreOpen`, subscription closes relation editor on details close.
  - `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`: AppAutomation Headless scenario pattern.
  - `src/Unlimotion.Test/Unlimotion.Test.csproj` и `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`: TUnit runner подтвержден.
  - `src/Unlimotion.Android/Unlimotion.Android.csproj`: `net10.0-android`, `Avalonia.Android` и отсутствие AndroidX Activity dependency подтверждены.
- Depth checklist:
  - Scope drift / unrelated changes: spec ограничивает changes шестью файлами; прочий churn запрещен.
  - Acceptance criteria: покрывают open, close, no selected task и existing toggle regression.
  - Validation evidence: planned targeted unit/UI TUnit runs + solution build + Android build when SDK is available; video fallback explicitly justified.
  - Unsupported claims: Android callback compatibility теперь ограничена legacy override + guarded API 33+ callback и Android build evidence.
  - Regression / edge case: no-task, not-ready app, relation editor side effect описаны.
  - Comments/docs/changelog: комментарии не планируются; changelog не нужен для small bugfix до отдельного release workflow.
  - Hidden contract change: public behavior меняется только на Android back gesture при selected task.
  - Manual-review challenge: отдельное ревью, вероятно, спросит почему toggle, а не close-only, и чем доказан Android gesture path; ответ зафиксирован в alternatives, callback design и Android build check.
- No-findings justification: spec содержит конкретный design, границы, tests и fallback evidence; блокирующих неопределенностей нет.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video artifact может быть недоступен в локальном Headless runner. | В EXEC проверить наличие штатной записи; иначе явно отчитаться fallback + logs. | accepted-risk |
| LOW | environment | Android build может быть недоступен локально без установленного Android SDK. | В EXEC запустить Android build, если SDK доступен; иначе указать точную средовую причину и next-best compile evidence. | accepted-risk |

- Fixed before continuing: TUnit command syntax заменен на `--treenode-filter`; Android callback design уточнен legacy + API 33+ guarded path; UI state selector зафиксирован через `CurrentTaskDetailsScrollViewer`.
- Checks rerun: SPEC linter/rubric и post-SPEC review повторены вручную по owner-документам после исправлений.
- Needs human: подтверждение спеки фразой `Спеку подтверждаю`.
- Residual risks / follow-ups: выбрать актуальный Android back callback по target API в EXEC.

### Post-EXEC Review
- Статус: PASS с зафиксированными environment caveats после review-refresh
- Scope reviewed: approved spec `specs/2026-06-02-android-back-task-card.md`, `git status --short`, `git diff --stat`, `git diff --check`, relevant implementation/test/spec diff, targeted TUnit results, targeted Headless UI results, Android API compile-probe, attempted Android/full-suite runs, docs/changelog impact, owner-documents (`quest-mode`, `quest-governance`, `review-loops`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, local `AGENTS.override.md`)
- Decision: можно завершать EXEC; блокирующих code/test findings по реализованному scope не осталось.
- Review passes:
  - Scope/Evidence pass: изменения ограничены запланированными application/viewmodel/android/test/spec файлами; `git status --short`, `git diff --stat`, relevant diff и `git diff --check` просмотрены; whitespace errors нет.
  - Contract pass: Android слой вызывает общий app-level facade; ViewModel владеет toggle-логикой; no-task/not-ready paths возвращают `false` и сохраняют platform default.
  - Adversarial risk pass: проверены API 33+ callback registration/unregistration, legacy fallback, UI-thread dispatch, no-current-task behavior, существующий relation editor side effect через `DetailsAreOpen=false`.
  - Re-review after fixes / Fix and re-review: исправлен UI test assertion после обнаружения, что headless resolver находит скрытый `CurrentTaskDetailsScrollViewer`; Android callback registration теперь dispose-ит callback при failed registration; deprecated `base.OnBackPressed()` fallback закрыт локальным `CA1422` suppression и подтвержден compile-probe без warnings; review-refresh исправил stale approval marker и сделал `Scope reviewed` полным по `review-loops.md`.
  - Stop decision: targeted unit/UI tests и main desktop build зеленые; Android project/full-suite limitations задокументированы как environment caveats.
- Evidence inspected:
  - `src/Unlimotion.ViewModel/MainWindowViewModel.cs`: `TryHandleTaskCardBackGesture()` toggles `DetailsAreOpen` only when `CurrentTaskItem != null`.
  - `src/Unlimotion/App.axaml.cs`: static facade dispatches to UI thread and returns `false` if ViewModel is not ready.
  - `src/Unlimotion.Android/MainActivity.cs`: legacy `OnBackPressed()` and API 33+ `IOnBackInvokedCallback` paths share `HandleSystemBack()`.
  - `src/Unlimotion.Test/MainWindowViewModelTests.cs`: open->close, closed->open, no-current-task tests.
  - `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` and `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`: task-card UI regression through existing toggle and `CurrentTaskDetailsScrollViewer`.
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated tracked changes detected; only one new spec file remains untracked by design.
  - Acceptance criteria: covered by ViewModel unit tests for open/close/no-task and Headless UI regression for close/reopen card state.
  - Validation evidence: commands and outcomes recorded below.
  - Unsupported claims: actual Android project build could not complete in this environment; Android API compatibility is supported by a focused temporary `net10.0-android` compile-probe.
  - Regression / edge case: no-task/not-ready app paths preserve default back; relation editor close-on-details-close remains through existing subscription.
  - Comments/docs/changelog: changelog не нужен для small локального bugfix без release workflow; docs impact ограничен рабочей spec; one pragma is narrowly scoped to deprecated platform fallback.
  - Hidden contract change: behavior change is limited to Android system back with a selected task.
  - Manual-review challenge: likely questions are Android build evidence and why toggle rather than close-only; both are documented in spec and implementation.
- PASS justification: implemented diff matches approved design, targeted regression tests pass, mandatory review-loop fields are now explicit, and remaining non-green checks are environment/full-suite caveats outside the changed behavior.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | review-completeness | Initial Post-EXEC review text did not explicitly list all mandatory `review-loops.md` scope items and left `Approval` as pending after the user had already approved the spec. | Update Post-EXEC scope wording, approval state, and journal; re-check spec diff. | fixed |
| LOW | environment | `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -f net10.0-android --no-restore -v:minimal` did not complete within 180s and produced no diagnostics in the final attempt. | Report caveat and keep focused Android API compile-probe evidence. | accepted-risk |
| LOW | environment | Full `src\Unlimotion.Test` run timed out after 10 minutes with no intermediate diagnostics. | Report caveat; targeted regression test passed. | accepted-risk |
| LOW | existing-suite | Full Headless UI suite fails outside the new scenario with Avalonia cross-thread access during existing task deletion/relations refresh. | Report caveat; targeted new UI scenario passed. | accepted-risk |
| LOW | evidence | No video artifact is produced by the local Headless TUnit runner pattern used here. | Use HTML reports and command output as next-best UI evidence. | accepted-risk |
| LOW | environment | Review-refresh targeted unit rerun in managed sandbox was blocked by NuGet repository-signature SSL, Avalonia telemetry write denial to AppData, and 300s timeouts even after `--no-restore`/`UsedAvaloniaProducts=` attempts. | Report current managed-sandbox caveat; keep prior same-day passing targeted TUnit/Headless reports as evidence. | accepted-risk |

- Fixed before final report:
  - Review-refresh updated mandatory Post-EXEC scope wording and replaced stale approval text.
  - Reworked Headless UI assertion from hidden-control disappearance to `DetailsPaneToggleButton.IsToggled` close/open state plus details-pane re-resolution after reopen.
  - Disposed API 33+ callback if registration throws.
  - Added local `CA1422` suppression around `base.OnBackPressed()` platform fallback after compile-probe identified the analyzer warning.
- Checks rerun:
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/*TaskCardBack*"` -> PASS, 3 total, 0 failed, report `src\Unlimotion.Test\bin\Debug\net10.0\TestResults\Unlimotion.Test-windows-net10.0-report.html`.
  - `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/Task_card_can_be_closed_and_reopened_from_details_toggle"` -> PASS, 1 total, 0 failed, report `tests\Unlimotion.UiTests.Headless\bin\Debug\net10.0\TestResults\Unlimotion.UiTests.Headless-windows-net10.0-report.html`.
  - `dotnet build src\Unlimotion\Unlimotion.csproj --no-restore -v:minimal` -> PASS, 0 errors; only git line-ending warnings from current working copy.
  - Temporary `net10.0-android` compile-probe for `[Activity(EnableOnBackInvokedCallback = true)]`, `IOnBackInvokedCallback`, `IOnBackInvokedDispatcher.PriorityDefault`, and local `CA1422` suppression -> PASS, 0 warnings, 0 errors.
  - `git diff --check` -> PASS, only line-ending conversion notices.
  - Review-refresh `git diff --check` -> PASS, only line-ending conversion notices.
  - Existing same-day report artifacts remain present: `src\Unlimotion.Test\bin\Debug\net10.0\TestResults\Unlimotion.Test-windows-net10.0-report.html` (2026-06-02 21:39:44 +03:00) and `tests\Unlimotion.UiTests.Headless\bin\Debug\net10.0\TestResults\Unlimotion.UiTests.Headless-windows-net10.0-report.html` (2026-06-02 17:46:01 +03:00).
- Validation caveats:
  - `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -f net10.0-android --no-restore -v:minimal` -> timed out after 180s with no diagnostics.
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj` -> timed out after 10 minutes with no diagnostics.
  - `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj` -> FAIL outside new scenario due existing Avalonia cross-thread crash in `TaskItemViewModel.SynchronizeTaskCollection` / `UnifiedTaskStorage.Delete`.
  - Review-refresh targeted unit rerun in managed sandbox:
    - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/*TaskCardBack*"` -> FAIL with `NU1301` SSL/authentication errors while reading NuGet repository signatures.
    - escalated retry of the same command -> timed out after 300s with no useful output.
    - `dotnet run --no-restore --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/*TaskCardBack*"` -> FAIL with `AvaloniaStatsTask` `UnauthorizedAccessException` writing `C:\Users\Kibnet\AppData\Local\AvaloniaUI\BuildServices\buildtasks.log`.
    - escalated `--no-restore` retry -> timed out after 300s with no useful output.
    - `dotnet run --no-restore -p:UsedAvaloniaProducts= --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/*TaskCardBack*"` -> timed out after 300s after build warnings.
- Unrelated changes: none detected in tracked files; untracked `specs/2026-06-02-android-back-task-card.md` belongs to this QUEST.
- Needs human: нет.
- Residual risks / follow-ups: run the full Android project build and full suites in a clean CI/Android SDK environment; investigate existing Headless full-suite cross-thread failure separately if it is not already tracked.

## Approval
Подтверждено пользователем фразой: "Спеку подтверждаю".

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создать working spec для Android back gesture | 0.92 | Подтверждение спеки для EXEC | Запросить `Спеку подтверждаю` | Да | Да, требуется утверждение до кода | Central QUEST gate запрещает менять код до утверждения; дизайн и проверки зафиксированы | `specs/2026-06-02-android-back-task-card.md` |
| SPEC | Итеративный spec review до non-LOW cleanup | 0.94 | Подтверждение спеки для EXEC | Отчитаться о review result | Нет для review; да для EXEC перехода | Нет, текущая задача review выполнена автономно | Исправлены non-LOW замечания по TUnit runner, Android callback specificity и UI selector; остались только LOW residual risks | `specs/2026-06-02-android-back-task-card.md` |
| EXEC | Реализовать Android back gesture -> task-card toggle | 0.86 | Чистый Android project build в текущем окружении | Запустить targeted/full проверки и post-EXEC review | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | Логика вынесена во ViewModel, Android слой вызывает общий facade, default back сохраняется для no-task/not-ready | `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.Android/MainActivity.cs` |
| EXEC | Добавить regression coverage и validation evidence | 0.84 | Full-suite/Android build остаются environment caveats | Обновить spec и финально отчитаться | Нет | Нет | Targeted unit/UI проверки проходят; full-suite caveats задокументированы без расширения scope | `src/Unlimotion.Test/MainWindowViewModelTests.cs`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`, `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`, `specs/2026-06-02-android-back-task-card.md` |
| EXEC | Instruction-driven Post-EXEC review refresh | 0.88 | Чистый targeted rerun в текущей managed sandbox невозможен из-за NuGet/Avalonia/timeouts | Отчитаться пользователю | Нет | Да, пользователь запросил `Сделай exec review по инструкции` | Review-loop сверил scope/evidence/contract/adversarial risk, исправил документальные gaps в Post-EXEC review и зафиксировал свежие sandbox rerun caveats | `specs/2026-06-02-android-back-task-card.md` |
