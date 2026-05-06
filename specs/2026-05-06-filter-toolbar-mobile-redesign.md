# Responsive Filter Toolbar For Narrow Screens

## 0. Метаданные
- Тип (профиль): `delivery-task`; профили `dotnet-desktop-client`, `ui-automation-testing`; контекст `testing-dotnet`.
- Владелец: Codex / пользователь.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка.
- Ограничения: QUEST mode; до подтверждения менять только эту спецификацию; UI-facing изменение обязано иметь UI test coverage и релевантный запуск UI-тестов.
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Projects\My\Agents\instructions\core\quest-mode.md`, `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`, `C:\Projects\My\Agents\instructions\profiles\ui-automation-testing.md`, `specs/2026-03-30-font-size-ui-layout-hardening.md`.

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Передизайнить верхнюю панель фильтров на task-вкладках так, чтобы на телефонной/узкой ширине поле поиска не занимало единственное доступное место и не вытесняло фильтры.

Outcome contract:
- Success means: на узкой ширине поиск и фильтры видимы без горизонтального вытеснения; поиск получает удобную строку, а фильтры переносятся в отдельную строку и остаются кликабельными.
- Итоговый артефакт / output: scoped XAML/C# правка панели фильтров и headless UI-тест, проверяющий narrow layout.
- Stop rules: остановиться после реализации, targeted UI test, build/test проверки и post-EXEC review; если адаптивное поведение требует выбора между разными UX-моделями с равными tradeoff, вернуться к пользователю.

## 2. Текущее состояние (AS-IS)
- Основной UI живёт в `src/Unlimotion/Views/MainControl.axaml`.
- Панель фильтров каждой task-вкладки построена как `Grid Classes="FilterToolbar" ColumnDefinitions="*,Auto"`.
- Левая колонка содержит `WrapPanel Classes="FilterToolbarItems"` с фильтрами.
- Правая `Auto`-колонка содержит `searchControl:SearchBar Classes="FilterToolbarSearch"`.
- `SearchBar` оборачивает `SearchControl`, а `SearchControlStyles.axaml` задаёт `MinWidth="{DynamicResource AppSearchBarMinWidth}"`; дефолтное значение в `App.axaml` равно `328`.
- На узкой ширине `Auto`-колонка поиска сохраняет минимум 328px и забирает пространство, из-за чего фильтры в левой колонке сжимаются/вытесняются и панель становится непригодной на телефоне.
- Уже есть Avalonia Headless UI tests в `src/Unlimotion.Test`, включая responsive-паттерны в `SettingsControlResponsiveUiTests.cs`.

## 3. Проблема
Одна корневая проблема: layout фильтров жёстко оптимизирован под desktop-ширину и не имеет narrow/mobile режима, поэтому фиксированная минимальная ширина поиска доминирует над фильтрами.

## 4. Цели дизайна
- Разделение ответственности: сохранить VM/search/filter state без изменений; менять только представление и UI-тест.
- Повторное использование: применить один адаптивный механизм ко всем `Grid.FilterToolbar`, а не копировать отдельную логику по вкладкам.
- Тестируемость: добавить headless UI regression test на узкую ширину с проверкой bounds и видимости контролов.
- Консистентность: оставить desktop-поведение с поиском справа от фильтров, где хватает ширины.
- Обратная совместимость: не менять bindings, команды, persisted settings, фильтрацию, сортировку и automation id существующих элементов.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем алгоритмы фильтрации, поиска, сортировки или reset-команды.
- Не меняем тексты, локализацию, ресурсы темы и persisted settings.
- Не переделываем табы, task tree, details pane или графовый режим.
- Не вводим новый дизайн-системный компонент вне нужд этой панели.
- Не удаляем существующие automation id.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> оставить декларативную структуру toolbar, добавить недостающие стабильные automation id для search editor при необходимости теста.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> централизованно переключать все `Grid.FilterToolbar` между desktop и narrow раскладкой по фактической ширине.
- `src/Unlimotion/Views/SearchControl/SearchControl.axaml` -> при необходимости дать search editor стабильный `AutomationId`.
- `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` -> headless regression coverage для узкого экрана.

### 6.2 Детальный дизайн
- Потоки данных: bindings `Search.SearchText`, filter states и команды не меняются.
- Контракты / API: публичные VM API не меняются.
- Output contract / evidence rules: evidence = UI-тест с окном телефонной ширины и проверкой, что search bar не выходит за ширину content, а фильтры расположены ниже поиска и имеют ненулевые bounds.
- Границы сохранения поведения: desktop-ширина сохраняет текущую модель `фильтры слева / поиск справа`; narrow-ширина переключает toolbar в две строки `поиск сверху / фильтры снизу`.
- Обработка ошибок: если toolbar ещё не измерен (`Bounds.Width <= 0`), оставить desktop layout до следующего size/layout pass.
- Производительность: переключение выполняется на size/layout событиях `MainControl`; количество toolbar небольшое, обход visual descendants допустим.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Threshold: narrow режим включается при ширине toolbar около телефонной/compact зоны, ориентир `<= 520px`.
- Desktop режим:
  - `ColumnDefinitions="*,Auto"`;
  - `RowDefinitions="Auto"`;
  - filters row/column = `0/0`;
  - search row/column = `0/1`, `HorizontalAlignment=Right`.
- Narrow режим:
  - `ColumnDefinitions="*"`;
  - `RowDefinitions="Auto,Auto"`;
  - search row/column = `0/0`, `HorizontalAlignment=Stretch`, width limited by toolbar width;
  - filters row/column = `1/0`, `HorizontalAlignment=Left`, `WrapPanel` продолжает переносить элементы.
- Search `MinWidth` в narrow режиме не должен принудительно превышать фактическую ширину toolbar.

## 8. Точки интеграции и триггеры
- `MainControl` после `InitializeComponent()` подписывается на изменение размеров/раскладки и вызывает обновление toolbar layout.
- При смене вкладки или изменении visual tree следующий layout pass должен применить правила к текущему `Grid.FilterToolbar`.
- UI test создаёт `MainControl` с `MainWindowViewModel`, открывает narrow window и проверяет активную task-вкладку.

## 9. Изменения модели данных / состояния
- Новых VM/model полей нет.
- Persisted state не меняется.
- Calculated UI-only state: текущий режим toolbar (`desktop`/`narrow`) может храниться только в свойствах visual controls/classes.

## 10. Миграция / Rollout / Rollback
- Первый запуск: без миграции.
- Обратная совместимость: XAML bindings и команды прежние.
- Rollback: вернуть `MainControl.axaml`/`.cs` к прежнему статическому `ColumnDefinitions="*,Auto"` и удалить новый UI-тест.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - На ширине около `360px` поиск не вытесняет фильтры: reset/filter controls имеют ненулевые bounds и находятся ниже строки поиска.
  - Search editor не выходит за правую границу `MainControl` на narrow width.
  - На desktop width поиск остаётся в одной строке справа от фильтров.
  - Search binding и reset/filter команды не меняются.
- Какие тесты добавить/изменить:
  - Добавить Avalonia.Headless UI test для `MainControl` narrow toolbar layout.
  - Желательно добавить desktop assertion в тот же тестовый класс, чтобы закрепить отсутствие regressions на широкой раскладке.
- Characterization tests / contract checks для текущего поведения: desktop assertion фиксирует прежнюю модель `filters + search в одной строке`.
- Базовые замеры до/после для performance tradeoff: не применимо, изменение layout-only и не создаёт дорогих операций.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --no-progress`
  - `dotnet build`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --no-progress`
- Stop rules для test/retrieval/tool/validation loops:
  - Сначала targeted UI test до зелёного результата.
  - Затем build.
  - Затем полный тестовый проект, если локально выполним; если нет, зафиксировать точную причину и ближайшую успешную проверку.

## 12. Риски и edge cases
- Риск: скрытые вкладки создаются лениво, и toolbar может появиться после первичного layout pass. Смягчение: обновлять layout на size/layout событии и при visual tree changes через повторный вызов после tab selection.
- Риск: прямое присваивание `RowDefinitions`/`ColumnDefinitions` на каждом pass может создавать лишнюю работу. Смягчение: менять только при смене режима.
- Риск: `SearchControl` внутренне имеет фиксированный `MinWidth`; narrow режим должен явно ограничить/переопределить это только для toolbar search, не ломая прочие использования.
- Риск: телефонная ширина меньше 328px. Смягчение: search в narrow режиме получает доступную ширину toolbar, а не ресурсный минимум.

## 13. План выполнения
1. Добавить UI-тест, который воспроизводит narrow overflow/вытеснение на `MainControl`.
2. Реализовать адаптивное переключение `Grid.FilterToolbar` в `MainControl`.
3. При необходимости добавить стабильный `AutomationId` search editor/toolbar без удаления существующих selectors.
4. Запустить targeted UI test и исправить layout до прохождения.
5. Запустить build и полный тестовый проект либо зафиксировать невозможность полного прогона.
6. Выполнить post-EXEC review и обновить журнал спеки.

## 14. Открытые вопросы
Нет блокирующих вопросов. UX-выбор для narrow режима принят автономно: поиск сверху, фильтры снизу, потому что он сохраняет доступность обеих групп контролов и не меняет фильтрующую логику.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`; контекст `testing-dotnet`.
- Выполненные требования профиля:
  - UI-поток не блокируется синхронными долгими операциями.
  - UI-facing изменение сопровождается headless UI test coverage.
  - Стабильные selectors сохраняются, новые selectors добавляются через `AutomationId`.
  - Перед завершением планируется `dotnet build` и `dotnet test`; targeted UI test обязателен.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Минимальные XAML-атрибуты для toolbar/search selectors при необходимости | Стабильность UI-теста и поддержка layout |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Адаптивное переключение desktop/narrow layout для `Grid.FilterToolbar` | Исправить узкий экран без VM changes |
| `src/Unlimotion/Views/SearchControl/SearchControl.axaml` | AutomationId для search editor при необходимости | Стабильный UI selector |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | Новый Avalonia.Headless regression test | Обязательное UI coverage |
| `specs/2026-05-06-filter-toolbar-mobile-redesign.md` | Журнал EXEC и review | QUEST traceability |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Узкая ширина | Search `Auto`-колонка с min 328px вытесняет filters | Search занимает первую строку, filters остаются видимыми ниже |
| Desktop ширина | Filters слева, search справа | То же поведение сохраняется |
| Search/filter logic | Bindings в VM | Без изменений |
| UI tests | Нет покрытия narrow task toolbar | Headless UI regression test |

## 18. Альтернативы и компромиссы
- Вариант: уменьшить глобальный `AppSearchBarMinWidth`.
- Плюсы: самая маленькая правка.
- Минусы: меняет desktop sizing во всех местах и не решает полностью ширину меньше нового минимума.
- Почему выбранное решение лучше в контексте этой задачи: адаптивная раскладка исправляет именно toolbar на телефоне и сохраняет desktop-контракт.

- Вариант: вернуть search внутрь общего `WrapPanel`.
- Плюсы: минимум кода.
- Минусы: прежние specs уже фиксировали, что общий перенос поиска с фильтрами ломает desktop toolbar при больших шрифтах.
- Почему выбранное решение лучше в контексте этой задачи: две управляемые раскладки дают предсказуемую геометрию.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, ошибки, perf и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Данные не меняются, rollback простой, план scoped. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, UI coverage и команды указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Блокирующих вопросов нет, реализация small и bounded. |
| F. Соответствие профилю | 20 | PASS | .NET desktop и UI automation требования учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна UI-проблема и explicit Non-Goals. |
| 2. Понимание текущего состояния | 5 | Указаны конкретные файлы, layout и min-width ресурс. |
| 3. Конкретность целевого дизайна | 5 | Описаны desktop/narrow правила и threshold. |
| 4. Безопасность (миграция, откат) | 5 | Persisted state не трогается, rollback понятен. |
| 5. Тестируемость | 5 | Есть targeted UI test и проверочные команды. |
| 6. Готовность к автономной реализации | 5 | Вопросов нет, план исполним в рамках спеки. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлены явные desktop acceptance criteria, риск ленивого создания вкладок и ограничение `SearchControl.MinWidth` в narrow режиме.
- Что осталось на решение пользователя: только подтверждение перехода в EXEC.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: narrow UI-тест расширен на все task-вкладки; обработчик `SelectionChanged` ограничен реальным переключением tabs; добавлена реакция на `Bounds` самих toolbar, чтобы details-pane resize тоже переводил панель в narrow mode.
- Что проверено дополнительно для refactor / comments: diff review подтвердил отсутствие VM/search/filter behavioral changes; `HEAD:MainControl.axaml` уже содержит `EmojiText="{Binding TaskItem.Title}"`, поэтому падение старого structural-теста не связано с этой правкой.
- Остаточные риски / follow-ups: полный `Unlimotion.Test` сейчас падает 1/280 на существующем stale structural-тесте `TaskListRepeaterMarker_XamlTemplates_AddMarkerBeforeEveryInlineTitle`, который ищет старый `Text="{Binding TaskItem.Title, ...}"` вместо текущего `EmojiText`; targeted responsive UI tests проходят.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction stack и routing | 0.95 | Нет | Собрать UI-контекст | Нет | Нет | Локальный `AGENTS.md` требует central QUEST stack и локальный override требует UI tests | `AGENTS.md`, `AGENTS.override.md`, `C:\Projects\My\Agents\instructions\*` |
| SPEC | AS-IS анализ toolbar | 0.9 | Нет | Зафиксировать спецификацию | Нет | Нет | Найдено сочетание `Grid ColumnDefinitions="*,Auto"` и `SearchControl MinWidth=328`, объясняющее narrow bug | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/SearchControl/*`, `src/Unlimotion/App.axaml` |
| SPEC | Создание спеки и quality gate | 0.92 | Нет | Запросить подтверждение пользователя | Да | Да, ожидается фраза `Спеку подтверждаю` | QUEST запрещает кодовые правки до явного подтверждения спеки | `specs/2026-05-06-filter-toolbar-mobile-redesign.md` |
| EXEC | Подтверждение спеки и UI regression test | 0.9 | Нет | Реализовать adaptive layout | Нет | Да, пользователь подтвердил `Спеку подтверждаю` | Добавлен headless UI-тест на narrow и wide toolbar geometry до/вместе с правкой | `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` |
| EXEC | Реализация adaptive filter toolbar | 0.86 | Результаты тестов | Запустить targeted UI test | Нет | Нет | Один code-behind механизм переключает все `Grid.FilterToolbar` между desktop и narrow без VM changes | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs` |
| EXEC | Verification | 0.88 | Full suite имеет unrelated stale structural failure | Выполнить post-EXEC review | Нет | Нет | Targeted responsive UI tests прошли 2/2; `Unlimotion.Test.csproj` build прошёл; `src/Unlimotion.sln` build не уложился в 5 минут; полный `Unlimotion.Test` прошёл 279/280 и упал на существующем XAML structural-тесте, не связанном с diff | `src/Unlimotion.Test/Unlimotion.Test.csproj`, `src/Unlimotion.sln`, `src/Unlimotion.Test/TaskListRepeaterMarkerUiTests.cs` |
| EXEC | Post-EXEC review | 0.92 | Нет блокирующих неизвестных | Передать итог пользователю | Нет | Нет | После review расширено покрытие на все task-вкладки, добавлен guard для tab selection handler и bounds-subscription для resize от details pane; targeted tests и diff check повторно проходят | `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`, `src/Unlimotion/Views/MainControl.axaml.cs`, `specs/2026-05-06-filter-toolbar-mobile-redesign.md` |
| EXEC | Commit/PR preparation | 0.95 | Нет | Commit, push, draft PR | Нет | Да, пользователь попросил `сделай коммит и pr` | Проверены `gh`, auth, branch/remotes; scope worktree относится к задаче; targeted UI tests 3/3, test project build and diff-check pass | `src/Unlimotion.Test/Unlimotion.Test.csproj`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`, `src/Unlimotion/Views/MainControl.axaml.cs` |
