# Отступы дат планирования в карточке задачи

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до подтверждения менять только эту спецификацию; реализация после фразы `Спеку подтверждаю`
- Связанные ссылки: запрос пользователя: "Пропали отступы дат начала и окончания. Надо сделать как в длительности"

Если секция не применима, явно указано `Не применимо` с причиной.

## 1. Overview / Цель
Вернуть внутренние отступы в полях плановой даты начала и плановой даты окончания карточки задачи, сделав их визуально консистентными с полем плановой длительности.

Outcome contract:
- Success means: `CurrentTaskPlannedBeginPicker` и `CurrentTaskPlannedEndPicker` имеют такой же внутренний `Padding`, как `CurrentTaskPlannedDurationTextBox`; compact layout карточки задачи не ломается.
- Итоговый артефакт / output: XAML-стиль для planning date pickers + headless UI regression test.
- Stop rules: остановиться после целевого фикса, успешного targeted UI test, `dotnet build` и доступного full test run либо явно зафиксированного объективного блокера.

## 2. Текущее состояние (AS-IS)
- Карточка задачи находится в `src/Unlimotion/Views/MainControl.axaml`.
- В секции `CurrentTaskPlanningSection` три группы планирования:
  - `CurrentTaskPlannedBeginPicker` (`CalendarDatePicker`)
  - `CurrentTaskPlannedDurationTextBox` (`TextBox`)
  - `CurrentTaskPlannedEndPicker` (`CalendarDatePicker`)
- Общие размеры задаются стилями `CalendarDatePicker.TaskPlanningValueControl` и `TextBox.TaskPlanningValueControl`.
- Сейчас оба стиля фиксируют `MinWidth`, `Height`, `MinHeight`, но только `TextBox` получает свой стандартный внутренний отступ из темы. Для `CalendarDatePicker` отступы визуально исчезли.
- Релевантные UI-тесты уже есть в `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`; они проверяют компактность planning row, размеры и отсутствие overflow, но не фиксируют внутренние отступы date picker controls.
- Локальный `AGENTS.override.md` требует использовать и обновлять UI-тесты для UI-facing изменений.

## 3. Проблема
Поля даты начала и даты окончания в карточке задачи визуально прижали текст/placeholder к краю, тогда как поле длительности сохраняет читаемый внутренний отступ. Это нарушает консистентность одного planning row.

## 4. Цели дизайна
- Разделение ответственности: XAML отвечает за визуальный стиль, UI-тест фиксирует регрессионный контракт.
- Повторное использование: использовать существующие классы `TaskPlanningValueControl`, не вводить новый контрол.
- Тестируемость: добавить headless UI assertion рядом с существующими layout-тестами карточки задачи.
- Консистентность: begin/end date controls должны совпадать с duration control по внутренним отступам.
- Обратная совместимость: не менять bindings, commands, automation ids, layout grouping и persisted model.

## 5. Non-Goals (чего НЕ делаем)
- Не менять формат дат, локализацию, команды быстрых дат или duration parsing.
- Не менять размеры planning groups, порядок полей, иконки quick action buttons или responsive поведение.
- Не переносить date picker на другой контрол.
- Не добавлять новые persisted поля и миграции.
- Не рефакторить всю карточку задачи.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> добавить явный padding для `CalendarDatePicker.TaskPlanningValueControl`, совпадающий с визуальным padding `TextBox.TaskPlanningValueControl`.
- `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` -> добавить regression test/или расширить desktop layout test, проверяющий, что begin/end pickers используют такой же padding, как duration field.

### 6.2 Детальный дизайн
- В XAML для `CalendarDatePicker.TaskPlanningValueControl` добавить `Padding`, равный `TextBox.TaskPlanningValueControl`.
- Если прямое сравнение `CalendarDatePicker.Padding` доступно в Avalonia 12, UI-тест должен сравнивать `beginPicker.Padding`, `endPicker.Padding` и `durationTextBox.Padding`.
- Если прямое свойство окажется недоступно при сборке, сохранить контракт на уровне ближайшего template/content presenter border, но не менять пользовательский UX-контракт.
- Output contract / evidence rules:
  - сначала добавить падающую UI-проверку на padding;
  - затем внести XAML-фикс;
  - targeted run должен пройти по `MainControlTaskCardLayoutUiTests`.
- Visual planning artifact для UI-facing изменений:

```text
Planning row, desktop:

[ Planned begin text has inner gap ][calendar]
[ Planned duration text has inner gap ][timer   ]
[ Planned end text has inner gap   ][finish  ]

Invariant: the visible left/right text inset in begin and end date fields matches the duration field.
```

- UI test video evidence: fallback. Текущий релевантный Avalonia.Headless/TUnit harness в репозитории не сохраняет video artifacts по принятому workflow; next-best evidence: deterministic headless UI assertion, targeted test output, build/full test output.
- Границы сохранения поведения: не менять `AutomationProperties.AutomationId`, bindings `PlannedBeginDateTime`, `PlannedEndDateTime`, `PlannedDuration`, command bindings и quick action menu items.
- Обработка ошибок: не применимо; визуальная правка без нового runtime error path.
- Производительность: не применимо; один style setter не влияет на вычислительную нагрузку.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Begin/end date field padding == duration field padding.
- Planning controls остаются высотой 40 и в существующей compact row.

## 8. Точки интеграции и триггеры
- Avalonia style resolution применяет `TaskPlanningValueControl` при создании карточки задачи.
- UI-тест создаёт `MainControl`, выбирает текущую задачу через fixture и проверяет resolved control properties после layout jobs.

## 9. Изменения модели данных / состояния
- Новые поля: нет.
- Persisted vs calculated: не применимо.
- Влияние на хранилище: нет.

## 10. Миграция / Rollout / Rollback
- Поведение при первом запуске: date picker fields получают явный padding сразу при загрузке XAML.
- Обратная совместимость: данные, команды и automation ids не меняются.
- Rollback: удалить style setter и regression test, если будет найдено несовместимое поведение темы Avalonia.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `CurrentTaskPlannedBeginPicker` и `CurrentTaskPlannedEndPicker` имеют padding, равный `CurrentTaskPlannedDurationTextBox`.
  - Existing desktop planning row compactness assertions продолжают проходить.
  - Existing phone/overflow assertions карточки задачи не получают регрессию.
- Какие тесты добавить/изменить:
  - добавить headless UI regression test в `MainControlTaskCardLayoutUiTests`, например `CurrentTaskCard_PlanningDatePickers_UseDurationFieldPadding`.
- Characterization tests / contract checks для текущего поведения:
  - сначала запустить новый тест до фикса и убедиться, что он падает на несоответствии padding.
- Visual acceptance для UI-facing изменений:
  - compare resolved padding values in headless UI;
  - existing layout helpers подтверждают, что row не распался и нет horizontal overflow.
- UI video evidence для UI-facing фич/багфиксов:
  - fallback: video не применимо, потому что текущий Avalonia.Headless/TUnit workflow не пишет видео; использовать targeted test output как next-best evidence.
- Базовые замеры до/после для performance tradeoff: не применимо.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/CurrentTaskCard_PlanningDatePickers_UseDurationFieldPadding"`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*"`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`
- Stop rules для test/retrieval/tool/validation loops:
  - не расширять scope за пределы planning controls, если targeted UI test и existing layout tests покрывают регрессию;
  - если full test run зависнет или упрётся в внешнее окружение, зафиксировать targeted + build evidence и объективный блокер.

## 12. Риски и edge cases
- Риск: у `CalendarDatePicker` в Avalonia 12 padding может применяться иначе, чем у `TextBox`.
  - Смягчение: подтвердить сборкой и headless UI assertion; при необходимости проверять template/content edge.
- Риск: увеличенный padding может уменьшить полезную ширину date text на узком экране.
  - Смягчение: не менять ширины групп; оставить существующие overflow/phone tests.
- Риск: слишком жёсткое сравнение padding сломается при будущей теме.
  - Смягчение: тест должен сравнивать локальный контракт внутри карточки задачи, а не глобальные theme defaults.

## 13. План выполнения
1. Добавить regression UI test, который сравнивает padding begin/end date pickers с duration textbox.
2. Запустить этот targeted test до фикса и зафиксировать ожидаемое падение.
3. Добавить явный `Padding` в `CalendarDatePicker.TaskPlanningValueControl`.
4. Запустить targeted test и весь `MainControlTaskCardLayoutUiTests`.
5. Запустить `dotnet build src/Unlimotion.sln`.
6. Запустить `dotnet test src/Unlimotion.sln` или явно зафиксировать блокер/timeout и next-best evidence.
7. Выполнить post-EXEC review-loop и исправить findings.

## 14. Открытые вопросы
Нет блокирующих вопросов. Для перехода к реализации нужна фраза `Спеку подтверждаю`.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`; overlay `ui-automation-testing`; context `testing-dotnet`.
- Выполненные требования профиля:
  - UI-facing изменение планируется через существующий Avalonia.Headless UI suite.
  - Automation ids сохраняются.
  - Тестовый план использует TUnit/Microsoft.Testing.Platform `--treenode-filter`.
  - Build/full test planned before final report.
  - Video evidence отмечено как fallback с причиной.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Добавить padding для `CalendarDatePicker.TaskPlanningValueControl` | Вернуть отступы begin/end date fields как у duration |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Добавить regression UI test для padding planning controls | Защитить визуальный контракт от повторной регрессии |
| `specs/2026-06-16-task-planning-date-padding.md` | Рабочая спецификация и журнал | QUEST governance |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Date picker padding | Begin/end выглядят без внутренних отступов | Begin/end получают такой же внутренний отступ, как duration |
| Layout | Planning row уже compact, но padding не проверяется | Planning row остаётся compact, padding покрыт тестом |
| Test coverage | Есть проверки размеров/позиции planning controls | Добавлена проверка visual inset contract |

## 18. Альтернативы и компромиссы
- Вариант: задать padding напрямую на каждом `CalendarDatePicker`.
  - Плюсы: максимально локально.
  - Минусы: дублирование, легче забыть при новом date picker.
  - Почему не выбран: стиль класса уже существует для обоих date picker fields.
- Вариант: создать общий custom style/control для planning fields.
  - Плюсы: единый API для date/duration.
  - Минусы: избыточно для small bugfix, выше риск layout regression.
  - Почему не выбран: один style setter решает проблему без рефакторинга.
- Вариант: менять глобальную тему `CalendarDatePicker`.
  - Плюсы: исправит все date pickers.
  - Минусы: неизвестный blast radius по фильтрам и настройкам.
  - Почему не выбран: проблема заявлена в карточке задачи, scope должен быть локальным.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals описаны. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, данные, rollout и rollback определены. |
| C. Безопасность изменений | 11-13 | PASS | План TDD, edge cases и порядок реализации зафиксированы. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, тесты и команды проверки перечислены. |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет блокирующих вопросов; scope малый и локальный. |
| F. Соответствие профилю | 20 | PASS | UI automation и .NET desktop требования учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна визуальная регрессия, явные non-goals. |
| 2. Понимание текущего состояния | 5 | Указаны конкретные XAML стили, controls и existing UI tests. |
| 3. Конкретность целевого дизайна | 5 | Решение сводится к локальному style setter и padding regression test. |
| 4. Безопасность (миграция, откат) | 5 | Нет данных/миграций; rollback прост. |
| 5. Тестируемость | 5 | Есть targeted failing test, suite run, build и full test plan. |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов, выбран минимальный вариант. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-task-planning-date-padding.md`; instruction stack `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, local `AGENTS.override.md`; planned files `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: просмотрены central routing docs, QUEST docs, local override, XAML planning styles и существующий task card UI test.
  - Contract pass: spec сохраняет bindings, automation ids и layout grouping; UI test и build/full test planned.
  - Adversarial risk pass: проверены риски неподдержанного padding property, узких экранов и чрезмерного scope; смягчения внесены.
  - Re-review after fixes / Fix and re-review: явных findings с обязательным исправлением после финальной записи нет.
  - Stop decision: PASS, требуется только пользовательское подтверждение EXEC.
- Evidence inspected:
  - `src/Unlimotion/Views/MainControl.axaml` styles `CalendarDatePicker.TaskPlanningValueControl`, `TextBox.TaskPlanningValueControl`
  - `src/Unlimotion/Views/MainControl.axaml` controls `CurrentTaskPlannedBeginPicker`, `CurrentTaskPlannedDurationTextBox`, `CurrentTaskPlannedEndPicker`
  - `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` existing layout assertions
- Depth checklist:
  - Scope drift / unrelated changes: planned changes limited to one XAML file and one UI test file.
  - Acceptance criteria: padding equality and no layout regression captured.
  - Validation evidence: commands planned; no EXEC tests run before approval.
  - Unsupported claims: direct `CalendarDatePicker.Padding` will be verified by build; fallback path documented.
  - Regression / edge case: narrow width and theme future-change risks captured.
  - Comments/docs/changelog: no comments/changelog planned.
  - Hidden contract change: no API, data, command or automation id change.
  - Manual-review challenge: reviewer may ask why no video; fallback reason is documented because current headless TUnit workflow does not persist video.
- No-findings justification: design is local, reversible, testable, and does not change user flow beyond restoring spacing.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence fallback must be explicit in final report if harness still lacks video artifacts. | Mention fallback and test output in EXEC final. | accepted-risk |

- Fixed before continuing: fallback evidence and visual planning artifact documented.
- Checks rerun: spec linter/rubric self-check updated after final spec edits.
- Needs human: user approval via `Спеку подтверждаю`.
- Residual risks / follow-ups: direct `Padding` property support must be confirmed during build.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec; `git status --short`; `git diff --stat`; relevant diff for `src/Unlimotion/Views/MainControl.axaml` and `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`; targeted failing/passing UI test output; full `MainControlTaskCardLayoutUiTests` output; `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj`; full `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj`; solution build blocker output.
- Decision: можно завершать; solution-wide mobile workload blocker зафиксирован как environment limitation, next-best evidence passed.
- Review passes:
  - Scope/Evidence pass: фактический diff ограничен XAML style setter, новым UI test и spec tracking; untracked/modified файлы соответствуют задаче.
  - Contract pass: begin/end date pickers получили padding, равный duration textbox; bindings, automation ids, quick action buttons и layout grouping не менялись.
  - Adversarial risk pass: проверены compact row, phone/overflow assertions, build затронутого тестового проекта и полный `Unlimotion.Test`; solution-wide build не прошёл только из-за отсутствующего `wasm-tools`.
  - Re-review after fixes / Fix and re-review: после XAML-фикса targeted test сменился с fail на pass; весь layout class и full test project пройдены.
  - Stop decision: PASS с зафиксированным environment blocker для solution-wide build/test.
- Evidence inspected:
  - До фикса: `CurrentTaskCard_PlanningDatePickers_UseDurationFieldPadding` failed; begin picker padding `4,4,4,4`, duration textbox padding `10,6,6,5`.
  - После фикса: targeted test passed, 1/1.
  - `MainControlTaskCardLayoutUiTests` passed, 16/16.
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj` passed.
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj` passed, 528/528.
  - `dotnet build src/Unlimotion.sln` failed before compilation of solution because Android/iOS projects require missing workload `wasm-tools`.
  - `git diff --check -- src/Unlimotion/Views/MainControl.axaml src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs specs/2026-06-16-task-planning-date-padding.md` exited 0; only line-ending warnings were reported for touched existing files.
- Depth checklist:
  - Scope drift / unrelated changes: нет; `git status --short` показывает только два рабочих файла и новую spec.
  - Acceptance criteria: выполнены; date picker padding равен duration field padding.
  - Validation evidence: targeted fail/pass, layout class, project build, full test project; solution build blocker documented.
  - Unsupported claims: padding values подтверждены failing test output and passing regression test.
  - Regression / edge case: compact row and phone layout covered by 16/16 layout tests; full test project passed.
  - Comments/docs/changelog: комментарии не добавлялись; changelog не нужен для small UI bugfix без release note request.
  - Hidden contract change: no data/API/commands/automation ids changed.
  - Manual-review challenge: reviewer may ask whether hardcoded padding should also be centralized; current scope matches existing local style class and observed duration value, with test guarding equality.
- No-findings justification: кодовый diff минимален, покрыт failing regression test and full available project tests; единственный неполный пункт validation связан с missing local workload, not code.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | `dotnet build src/Unlimotion.sln` blocked by missing `wasm-tools` workload for Android/iOS projects. | Report blocker and use project build + full `Unlimotion.Test` as next-best evidence. | accepted-risk |
| LOW | evidence | UI video evidence not produced by current Avalonia.Headless/TUnit workflow. | Report fallback and rely on deterministic headless UI assertion + HTML test report artifact. | accepted-risk |

- Fixed before final report: XAML padding set to `10,6,6,5`; regression test added and passed.
- Checks rerun: targeted UI test; `MainControlTaskCardLayoutUiTests`; test project build; full `Unlimotion.Test`; `git diff --check`.
- Validation evidence: listed above.
- Unrelated changes: none found.
- Needs human: no.
- Residual risks / follow-ups: install/restore `wasm-tools` workload to allow solution-wide mobile build/test locally.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | delivery-task planning | 0.86 | Подтверждение пользователя для EXEC; build-time подтверждение `CalendarDatePicker.Padding` | Запросить `Спеку подтверждаю` | Да | Да, ожидается подтверждение | Центральный QUEST требует SPEC перед кодом; выбран локальный XAML style fix + UI regression test | `specs/2026-06-16-task-planning-date-padding.md`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | approval received | 0.9 | Build-time подтверждение доступного свойства padding у `CalendarDatePicker` | Добавить regression UI test до XAML-фикса | Нет | Да, пользователь подтвердил `Спеку подтверждаю` | Переход в EXEC разрешён; начинаю с теста, чтобы воспроизвести регрессию | `specs/2026-06-16-task-planning-date-padding.md`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | failing regression test | 0.94 | Нет | Внести XAML style setter `Padding=10,6,6,5` для planning date pickers | Нет | Нет | Targeted test подтвердил дефект: date pickers `4,4,4,4`, duration textbox `10,6,6,5` | `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `src/Unlimotion/Views/MainControl.axaml` |
| EXEC | implementation and validation | 0.96 | Нет; solution-wide build ограничен missing `wasm-tools` workload | Завершить отчёт | Нет | Нет | XAML-фикс прошёл targeted, layout class, test project build and full `Unlimotion.Test`; post-EXEC review completed | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `specs/2026-06-16-task-planning-date-padding.md` |
