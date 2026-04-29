# All Tasks: emoji for important tasks

## 0. Метаданные
- Тип (профиль): delivery-task; profiles: `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до подтверждения спеки менять только этот файл; для UI-багфикса обязательно добавить/обновить UI test coverage и запустить релевантные UI tests
- Связанные ссылки: локальные инструкции `AGENTS.md`, `AGENTS.override.md`

Если секция не применима, явно указано `Не применимо` и короткая причина.

## 1. Overview / Цель
Исправить отображение emoji в дереве `All Tasks` для задач, помеченных как важные (`Wanted = true`).

Outcome contract:
- Success means: важная задача с emoji в любой позиции заголовка в `AllTasksTree` отображает emoji и при этом текст задачи остаётся визуально важным.
- Итоговый артефакт / output: минимальная правка Avalonia XAML/control usage и regression UI test.
- Stop rules: остановиться после таргетных UI tests, `dotnet build` и полного доступного тестового прогона; если полный прогон невозможен, зафиксировать причину и next-best checks.

## 2. Текущее состояние (AS-IS)
- `AllTasksTree` в `src/Unlimotion/Views/MainControl.axaml` использует `TreeDataTemplate` для `TaskWrapperViewModel`, который рендерит wrapper через общий `DataTemplate DataType="viewModel:TaskWrapperViewModel"`.
- Общий wrapper template выводит `ContentControl Content="{Binding TaskItem}"`.
- `TaskItemViewModel` template сейчас выводит весь `Title` одним `TextBlock` с `Classes.IsWanted="{Binding Wanted}"`.
- Стиль `TextBlock.IsWanted` делает текст жирным. Для emoji внутри того же текста, в том числе в середине `Title`, это создаёт проблему рендера: emoji не отображается/не получает emoji font path.
- В проекте уже есть `EmojiTextBlock`, который делит текст на emoji/non-emoji runs и для emoji задаёт `Noto Color Emoji` и `FontWeight.Normal`. Он используется для breadcrumbs.
- В `src/Unlimotion.Test/TaskImportanceUiTests.cs` уже есть Avalonia.Headless UI coverage для важного заголовка в `AllTasksTree` и relation tree, но она проверяет только bold-текст и не ловит emoji rendering path.

## 3. Проблема
Одна корневая проблема: в `AllTasksTree` emoji и важный текст рендерятся одним bold `TextBlock`, поэтому font/weight для emoji наследует состояние важности и emoji может не отображаться независимо от позиции emoji в `Title`.

## 4. Цели дизайна
- Разделение ответственности: XAML template отвечает за выбор emoji-aware text control; `TaskItemViewModel` не получает новую persisted/model state.
- Повторное использование: использовать существующий `EmojiTextBlock`, а не вводить новый control.
- Тестируемость: regression test должен проверять `AllTasksTree` на важную задачу с emoji и emoji-aware rendering path.
- Консистентность: сохранить существующее поведение `Wanted` как bold для текстовой части заголовка.
- Обратная совместимость: не менять публичные API, storage schema, automation ids и команды.

## 5. Non-Goals (чего НЕ делаем)
- Не менять модель данных `TaskItem` / `TaskItemViewModel`.
- Не менять алгоритм наследования emoji (`GetAllEmoji`, `Emoji`, `TitleWithoutEmoji`).
- Не переделывать все task list templates и graph template.
- Не менять UX важности, сортировку, фильтрацию, drag/drop или relation editing.
- Не обновлять screenshots/README media в рамках этого исправления.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> заменить title `TextBlock` в `TaskItemViewModel` template на `EmojiTextBlock` с `EmojiText="{Binding Title}"`, сохранив grid placement, vertical alignment и классы `IsWanted` / `IsCanBeCompleted`.
- `src/Unlimotion.Test/TaskImportanceUiTests.cs` -> обновить/добавить Avalonia.Headless regression coverage для важной задачи с emoji в `AllTasksTree`.

### 6.2 Детальный дизайн
- Поток данных остаётся прежним: `TaskWrapperViewModel -> TaskItemViewModel -> Title/Wanted`.
- `EmojiTextBlock` получает тот же `Title`, но внутри control emoji segments получают `Noto Color Emoji` и `FontWeight.Normal`.
- Non-emoji segments продолжают наследовать style/class состояния `Wanted`, поэтому важная задача остаётся bold.
- Output/evidence rules: тест должен найти rendered control внутри `AllTasksTree`, подтвердить привязку к нужному `TaskItemViewModel`, проверить `FontWeight.Bold` на control и отдельный emoji `Run` с `FontWeight.Normal`; regression case должен включать emoji в середине `Title`, например `Important 📚 task`.
- Обработка ошибок: дополнительных runtime errors не добавляется; binding fallback остаётся как у текущего XAML.
- Производительность: изменение локальное, parsing title уже используется в `EmojiTextBlock`; списки задач не получают новых подписок или пересчётов.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Важность (`Wanted`) влияет на визуальный вес текстовой части задачи.
- Emoji в заголовке должен оставаться emoji, даже если задача важная и emoji стоит в начале, середине или конце `Title`.
- Состояние completed/availability и repeater marker должны визуально остаться на прежних местах.

## 8. Точки интеграции и триггеры
- Триггер: Avalonia template creation для `TaskItemViewModel` внутри `AllTasksTree` и других мест, где используется общий template.
- Интеграция: `EmojiTextBlock.OnPropertyChanged(EmojiTextProperty)` строит `Inlines` из `EmojiTextHelper.Split`.

## 9. Изменения модели данных / состояния
- Новые поля: не применимо, правка только presentation layer.
- Persisted vs calculated: не применимо, storage не меняется.
- Влияние на хранилище: отсутствует.

## 10. Миграция / Rollout / Rollback
- Поведение при первом запуске: не применимо, XAML/template change применяется при старте UI.
- Обратная совместимость: сохраняются binding paths, automation ids и публичные типы.
- План отката: вернуть title element в `TaskItemViewModel` template к обычному `TextBlock` и откатить regression test.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В `AllTasksTree` важная задача с emoji в середине `Title` использует emoji-aware rendering path.
  - Emoji segment рендерится нормальным весом и emoji font family, несмотря на `Wanted = true`.
  - Не-emoji segments до и после emoji остаются в правильном порядке и не теряются.
  - Текстовая часть важной задачи остаётся `FontWeight.Bold`.
  - Existing repeater marker и relation tree важности не ломаются.
- Какие тесты добавить/изменить:
  - Обновить `TaskImportanceUiTests` или добавить отдельный Avalonia.Headless test рядом с ним.
  - Использовать тестовый заголовок с emoji в середине текста, чтобы не покрывать только prefix-сценарий.
  - Regression test сначала должен падать на текущем XAML, затем проходить после замены на `EmojiTextBlock`.
- Characterization tests / contract checks: сохранить/адаптировать проверку bold в `AllTasksTree` и `CurrentItemParentsTree`.
- Базовые замеры performance: не применимо, изменение UI template small scope.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/Unlimotion.Test/TaskImportanceUiTests/*"`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/Unlimotion.Test/TaskListRepeaterMarkerUiTests/*"`
  - `dotnet build`
  - `dotnet test` или принятый полный TUnit/MTP runner для решения, если `dotnet test` поддержан локально
- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted UI test падает после фикса, продолжать диагностику.
  - Если полный прогон недоступен из-за окружения/runner issue, выполнить доступные таргетные UI tests и build, затем явно указать ограничение.

## 12. Риски и edge cases
- `EmojiTextBlock` может не выставлять `Text`, поэтому существующие тесты/поиск по `TextBlock.Text` нужно адаптировать к `EmojiText`/`Inlines`.
- Общий `TaskItemViewModel` template используется не только в `AllTasksTree`; это ожидаемое улучшение для relation tree, но нужно проверить existing UI tests.
- Если binding class `IsWanted` на `EmojiTextBlock` не наследует style `TextBlock.IsWanted`, тест должен это поймать.

## 13. План выполнения
1. В EXEC сначала добавить/обновить headless UI regression test для важной задачи с emoji в `AllTasksTree`; убедиться, что он падает на текущем XAML.
2. Заменить title `TextBlock` в `TaskItemViewModel` template на `EmojiTextBlock`, сохранив layout/classes.
3. Запустить targeted UI tests.
4. Запустить `dotnet build`.
5. Запустить полный доступный test run или зафиксировать техническую причину, если runner/окружение не позволяют.
6. Выполнить post-EXEC review и исправить высокоуверенные проблемы в границах спеки.

## 14. Открытые вопросы
Нет блокирующих вопросов. Предположение: корректный продуктовый результат - сохранить полный `Title` как source для UI, но рендерить emoji segments через `EmojiTextBlock`.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - План не блокирует UI-поток.
  - План сохраняет automation-id/test selectors.
  - План включает обязательный UI regression test.
  - План включает запуск релевантных UI tests, build и полный доступный test run.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Заменить title `TextBlock` в `TaskItemViewModel` template на `EmojiTextBlock` | Emoji должен рендериться через emoji font/normal weight при `Wanted = true` |
| `src/Unlimotion.Test/TaskImportanceUiTests.cs` | Добавить/адаптировать headless UI assertions для emoji-aware title rendering | Regression coverage для бага в `AllTasksTree` |
| `specs/2026-04-28-all-tasks-important-emoji.md` | Рабочая спецификация и журнал | Требование QUEST |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `AllTasksTree` important title | Один bold `TextBlock` с полным `Title` | `EmojiTextBlock` с emoji runs normal/Noto Color Emoji и bold text segments, независимо от позиции emoji |
| UI tests | Проверяется bold важного заголовка | Проверяется bold + emoji-aware rendering path |
| Data/storage | Без изменений | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: выводить emoji отдельным `Label` через `Emoji`/`GetAllEmoji`, а текст через `TitleWithoutEmoji`.
- Плюсы: явно отделяет emoji от текста.
- Минусы: риск изменить существующую семантику отображения в hierarchical tree и relation tree, затронуть наследование emoji/дублирование.
- Почему выбранное решение лучше в контексте этой задачи: существующий `EmojiTextBlock` решает ровно проблему font/weight внутри одного title, минимально меняет layout и не трогает ViewModel/model contracts.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, дизайн-цели и Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, правила, state и rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Storage/API не меняются, план отката простой |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, UI tests и команды проверки указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | План малый, блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | UI automation и .NET desktop требования учтены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Баг, target UI и границы указаны |
| 2. Понимание текущего состояния | 5 | Описан template path и существующий `EmojiTextBlock` |
| 3. Конкретность целевого дизайна | 5 | Указан конкретный control/binding/test evidence |
| 4. Безопасность (миграция, откат) | 5 | Нет storage/API changes, rollback локальный |
| 5. Тестируемость | 5 | Есть regression UI test plan и команды |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов, scope small |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: после первичного анализа выбран `EmojiTextBlock` вместо отдельного split layout, чтобы не менять семантику `Title`/`GetAllEmoji`.
- Что осталось на решение пользователя: требуется только утверждение спеки фразой из блока Approval.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: новый nullable warning в test helper устранён; XAML contract test ужесточён так, чтобы проверять свойства именно на строке `EmojiTextBlock`.
- Что проверено дополнительно для refactor / comments: рефакторинга production-кода и новых комментариев нет; diff остаётся в границах `Non-Goals`.
- Остаточные риски / follow-ups: обычный parallel `dotnet test src/Unlimotion.sln --no-build` конфликтует между UI test modules; serial запуск `--max-parallel-test-modules 1` проходит 254/254.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор правил и маршрутизация | 0.95 | Нет | Создать spec | Да | Да, будет запрошено утверждение | AGENTS требует SPEC-first и UI tests для UI bugfix | `AGENTS.md`, `AGENTS.override.md`, central instructions |
| SPEC | AS-IS анализ UI template/tests | 0.9 | Нет | Сформировать TO-BE | Нет | Нет | Найден общий `TaskItemViewModel` template и существующий `EmojiTextBlock` | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/EmojiTextBlock.cs`, `src/Unlimotion.Test/TaskImportanceUiTests.cs` |
| SPEC | Draft spec + quality gate | 0.9 | Нет | Запросить утверждение | Да | Да, ожидается фраза `Спеку подтверждаю` | Scope малый, решение локальное и проверяемое UI tests | `specs/2026-04-28-all-tasks-important-emoji.md` |
| SPEC | Уточнение edge case | 0.95 | Нет | Запросить утверждение обновлённой спеки | Да | Да, пользователь уточнил позицию emoji | Добавлен явный acceptance/test case для emoji в середине `Title` | `specs/2026-04-28-all-tasks-important-emoji.md` |
| EXEC | Подтверждение спеки и failing test | 0.9 | Результат targeted test run | Запустить `TaskImportanceUiTests` до фикса | Нет | Да, пользователь подтвердил спеки фразой `Спеку подтверждаю` | Добавлен regression UI test для важной задачи с emoji в середине `Title` | `src/Unlimotion.Test/TaskImportanceUiTests.cs`, `specs/2026-04-28-all-tasks-important-emoji.md` |
| EXEC | TDD red и XAML fix | 0.9 | Результаты проверок после фикса | Запустить targeted UI tests | Нет | Нет | XAML contract test упал на обычном `TextBlock`; title template заменён на `EmojiTextBlock` | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/TaskImportanceUiTests.cs`, `specs/2026-04-28-all-tasks-important-emoji.md` |
| EXEC | Targeted UI validation | 0.9 | Результаты full build/test | Запустить полный доступный build/test workflow | Нет | Нет | `TaskImportanceUiTests` 4/4 и `TaskListRepeaterMarkerUiTests` 3/3 проходят после фикса | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/TaskImportanceUiTests.cs` |
| EXEC | Full validation | 0.95 | Финальный diff review | Выполнить post-EXEC review | Нет | Нет | `dotnet build src/Unlimotion.sln` прошёл; обычный parallel `dotnet test` конфликтовал между UI-модулями, serial `dotnet test --max-parallel-test-modules 1` прошёл 254/254 | `src/Unlimotion.sln`, `src/Unlimotion.Test/TaskImportanceUiTests.cs`, `tests/Unlimotion.UiTests.Headless`, `tests/Unlimotion.UiTests.FlaUI` |
| EXEC | Post-EXEC review | 0.95 | Нет | Завершить задачу | Нет | Нет | Проверен diff, scope и отсутствие новых production-комментариев; несвязанные `.vscode` изменения не тронуты | `specs/2026-04-28-all-tasks-important-emoji.md`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/TaskImportanceUiTests.cs` |
