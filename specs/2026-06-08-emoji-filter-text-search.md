# Поиск emoji-фильтров текстом

## 0. Метаданные
- Тип (профиль): UI-facing feature / desktop client
- Владелец: user
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения:
  - Сначала согласовать spec, затем выполнять EXEC.
  - UI-facing изменение обязано иметь UI-тесты.
  - Не ломать существующие фильтры по состоянию, датам, важности, блокировкам и кнопки reset.
  - Не терять текущую модель: открыть список, пролистать, поставить/убрать checkbox.
  - Не раздувать filter flyout на узком экране.
- Связанные ссылки: Не применимо, issue/PR не указан.

## 1. Overview / Цель
Заменить текущий выбор emoji-фильтра через компактный `ComboBox` на compact searchable multi-select dropdown: в закрытом состоянии он показывает выбранные emoji и overflow-count, первый клик открывает полный popup-список с checkbox-ами, повторный клик фокусирует пустое текстовое поле поиска, а ввод текста фильтрует открытый список.

Outcome contract:
- Success means:
  - В панелях фильтров есть compact multi-select dropdown для include и exclude emoji-фильтров.
  - В закрытом состоянии компонент показывает выбранные emoji в порядке текущего списка и `+N` для выбранных, которые не помещаются.
  - Первый клик по компоненту открывает popup/dropdown как обычный `ComboBox`: полный прокручиваемый список emoji-фильтров с checkbox-ами.
  - Повторный клик по компоненту при открытом dropdown ставит фокус в пустое текстовое поле поиска с placeholder.
  - Пока пользователь ничего не ввел в focused search field, список остается полным.
  - Ввод текста фильтрует открытый checkbox-список по названию, emoji и sort text.
  - Если совпадений нет, компонент показывает warning/empty-state и оставляет полный список доступным.
  - Checkbox-click не закрывает dropdown, чтобы можно было выбрать несколько фильтров.
  - `Esc`, keyboard navigation и pointer/touch сценарии работают предсказуемо.
  - Exclude-фильтр визуально отличим от include-фильтра.
  - Reset-фильтры продолжают сбрасывать выбранные emoji-фильтры.
- Итоговый артефакт / output:
  - Обновленный reusable multi-select search dropdown и места его использования.
  - Обновленная локализация placeholder/empty-state строк.
  - UI-тесты для summary, открытия полного popup-списка, click-to-search, фильтрации, warning/no-results, keyboard, toggle и reset.
- Stop rules:
  - Остановиться до утверждения spec фразой `Спеку подтверждаю`.
  - До утверждения не продолжать EXEC и не коммитить.

## 2. Текущее состояние (AS-IS)
- Emoji-фильтры в `MainControl.axaml` и `GraphControl.axaml` представлены парой `ComboBox`: include и exclude.
- Источники данных: `MainWindowViewModel.EmojiFilters` и `MainWindowViewModel.EmojiExcludeFilters`.
- Элемент фильтра представлен моделью `EmojiFilter` с `Title`, `Emoji`, `SortText`, `ShowTasks`, `Source`.
- Текущий `ComboBox` использует `DataTemplate` с `CheckBox IsChecked="{Binding ShowTasks}"`, поэтому пользователь может включать/выключать конкретные emoji-фильтры без общего reset.
- Старый `ComboBox` плохо подходит для длинных списков emoji-тегов: нужный фильтр приходится искать визуально/прокруткой.
- Панели фильтров используются в нескольких вкладках задач и в Roadmap, поэтому изменение должно быть единообразным.

## 3. Проблема
Пользователь не может быстро найти нужный emoji-фильтр текстом; при этом существующая dropdown/checkbox-семантика не должна быть потеряна.

## 4. Цели дизайна
- Разделение ответственности: вынести multi-select search dropdown emoji-фильтров в отдельный reusable-контрол.
- Повторное использование: применить один контрол для include/exclude во всех filter flyout, включая Roadmap.
- Тестируемость: дать стабильные `AutomationId` для summary field, popup/dropdown, search input и checkbox-list.
- Консистентность: сохранить текущий источник данных и модель `EmojiFilter.ShowTasks`.
- Обратная совместимость: не менять persisted state и формат задач.
- Без регрессии UX: сохранить открытие полного dropdown-списка, прокрутку, checkbox-включение/выключение и multi-select.
- Компактность: в закрытом состоянии занимать место одного поля и показывать selected emoji summary.
- Доступность: поддержать keyboard flow для открытия, перехода в поиск, навигации, закрытия и очистки.

## 5. Non-Goals (чего НЕ делаем)
- Не менять алгоритм извлечения emoji из задач.
- Не менять semantics include/exclude.
- Не добавлять новый экран управления тегами.
- Не переделывать весь filter toolbar.
- Не менять mobile navigation/tabs.
- Не сохранять search text в persisted state.
- Не закрывать dropdown после каждого checkbox-click.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `EmojiFilterMultiSelectSearchBox.axaml` -> summary/search text field, popup/dropdown, bounded scrollable checkbox-list, warning/empty-state и exclude styling.
- `EmojiFilterMultiSelectSearchBox.axaml.cs` -> open/focus/search state, summary text calculation, derived filtered view, keyboard behavior и pointer/touch behavior.
- `MainWindowViewModel.EmojiFilter` -> calculated `SearchText` для поиска по emoji, title и sort text.
- `MainControl.axaml` -> замена старых emoji `ComboBox` на новый multi-select search dropdown во всех task filter flyout.
- `GraphControl.axaml` -> такая же замена в Roadmap filter flyout.
- `Strings.resx` / `Strings.ru.resx` -> placeholder/empty-state strings для include/exclude.
- `MainControlFilterToolbarResponsiveUiTests.cs` -> UI coverage summary, full open, search mode, filtering, no-results warning, keyboard и toggle для task + Roadmap filter flyout.

### 6.2 Детальный дизайн
- Потоки данных:
  - ViewModel продолжает отдавать `EmojiFilters` / `EmojiExcludeFilters`.
  - Компонент вычисляет выбранные фильтры как `Filters.Where(filter => filter.ShowTasks)` в текущем порядке списка.
  - В закрытом состоянии поле показывает selected emoji summary в формате `🚀 🧰 ❌ +3`.
  - Summary берет выбранные элементы в текущем порядке списка; порядок выбора пользователем не сохраняется.
  - Summary всегда показывает как минимум один выбранный emoji, если он помещается; оставшиеся выбранные элементы сворачиваются в `+N`.
  - Фильтры без emoji, например служебный `All`, не участвуют в selected emoji summary и не добавляются в searchable popup list, если они не нужны для текущей checkbox-семантики.
  - Если в будущем появится выбираемый пользовательский фильтр без emoji, summary использует короткий trimmed title fallback и это должно быть покрыто отдельным тестом.
  - Первый click/tap по закрытому компоненту открывает отдельный popup/dropdown, визуально как обычный `ComboBox`.
  - Popup/dropdown anchored к компоненту и не должен закрывать родительский filter flyout при взаимодействии внутри popup.
  - Пока search text пустой, dropdown показывает полный список фильтров.
  - Повторный click/tap по полю при открытом dropdown фокусирует search input.
  - При входе в search mode search text всегда пустой, поле показывает placeholder, список остается полным до первого введенного символа.
  - В search mode ввод текста фильтрует checkbox-пункты по `EmojiFilter.SearchText`.
  - Если совпадений нет, показывается warning/empty-state, но полный список остается доступным ниже warning, чтобы пользователь мог продолжить выбор без очистки.
  - Checkbox каждого пункта напрямую привязан к `EmojiFilter.ShowTasks`.
  - Checkbox-click не закрывает dropdown.
  - Закрытие dropdown всегда очищает search text и возвращает поле к selected emoji summary.
- Контракты / API:
  - `Filters: IEnumerable?`
  - `Watermark: string?`
  - `NoMatchesText: string?`
  - `SummaryAutomationId: string?`
  - `SearchAutomationId: string?`
  - `DropDownAutomationId: string?`
  - `ListAutomationId: string?`
  - `NoMatchesAutomationId: string?`
  - `IsExclude: bool`
- Nested popup / parent flyout contract:
  - The component lives inside the existing filter panel flyout.
  - Opening the component popup must not close the parent filter panel.
  - Pointer/touch/keyboard interaction inside the component popup must not close the parent filter panel.
  - Clicking outside both the component popup and the parent filter panel may close the parent filter panel using existing flyout behavior.
  - `Esc` while search input is focused clears search text first if it is non-empty; second `Esc` closes the component popup; another `Esc` may close the parent filter panel.
  - Mouse wheel over the component popup scrolls the component list; mouse wheel over the parent filter panel outside popup scrolls the parent panel.
  - The component popup has bounded height and its own internal scroll.
  - The component popup must remain visible inside the usable viewport and must not be clipped by the parent filter flyout; if there is not enough space below the component, it may reposition like a normal combo popup.
- Keyboard contract:
  - `Enter` or `Space` on the closed component opens the full list.
  - `Enter`, `F2`, or typing a printable character while the component popup is open focuses search input; printable character starts search with that character.
  - `ArrowUp`/`ArrowDown` navigate visible checkbox items while search input is focused or list is focused.
  - `Space` toggles the focused checkbox item and keeps popup open.
  - `Esc` follows the nested popup contract above.
- Output contract / evidence rules:
  - Тест должен доказать, что первый клик открывает полный список.
  - Тест должен доказать, что повторный клик фокусирует empty search input.
  - Тест должен доказать, что поиск работает по части title/emoji/sort text.
  - Тест должен доказать, что search text сам по себе не меняет `ShowTasks`.
  - Тест должен доказать, что checkbox toggle работает в полном и отфильтрованном списке.
  - Тест должен доказать, что closed summary показывает selected emoji и `+N` overflow в порядке списка.
  - Тест должен доказать, что no-results warning появляется, но полный список остается доступным.
  - Тест должен доказать, что keyboard flow открывает, ищет, переключает checkbox и закрывает popup без закрытия parent filter panel раньше времени.
  - Тест должен доказать, что popup anchored к компоненту, не клипается parent flyout и остается в usable viewport на narrow viewport.
- Visual planning artifact для UI-facing изменений:

```text
Filter tags
[🚀 🧰 ❌ +3] v

first click opens popup:
  [x] 🚀 Запустить пилот
  [x] 🧰 Подготовить чеклист
  [x] ❌ Заблокировано
  [ ] 🧪 Проверка
  ... internal scroll ...

second click in field focuses search:
  [Найти тег                             ]
  [x] 🚀 Запустить пилот

no matches:
  [zzzz                                  ]
  Ничего не найдено
  [x] 🚀 Запустить пилот
  [x] 🧰 Подготовить чеклист
  [x] ❌ Заблокировано
  [ ] 🧪 Проверка

[❌ +2] v  <- exclude style
```

- UI test video evidence для UI automation задач:
  - Не применимо, video harness для этого flow не задан.
  - Fallback после EXEC: headless UI-тесты + попытка desktop screenshot через существующий `Unlimotion.ReadmeMedia --ux-review filter-toolbar`.
- Границы сохранения поведения:
  - `ShowTasks` остается единственным флагом выбора.
  - Reset продолжает сбрасывать существующие filter collections.
  - Точечное снятие фильтра checkbox-ом сохраняется.
  - Multi-select сохраняется: пользователь может выбрать несколько include и несколько exclude emoji-фильтров.
  - Search mode является дополнительным режимом и не должен мешать обычному открытию полного списка.
- Обработка ошибок:
  - Если search text пустой, показывается полный список фильтров.
  - Если совпадений нет, показывается warning/empty-state и полный список остается доступным.
  - Если выбранных фильтров нет, summary показывает placeholder/watermark.
- Производительность:
  - Поиск выполняется по текущей in-memory коллекции.
  - Дополнительного persistent index не требуется.
  - Dropdown list должен иметь bounded height и внутренний scroll, чтобы длинный список не раздувал filter flyout.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Include-filter: выбранный emoji-фильтр должен показывать задачи с этим emoji/tag.
- Exclude-filter: выбранный exclude-фильтр должен исключать задачи с этим emoji/tag.
- `EmojiFilter.SearchText` формируется как `Emoji + Title + SortText`, чтобы пользователь мог искать и по символу, и по читаемому названию.
- Checkbox toggle является каноническим действием выбора: `checked -> ShowTasks=true`, `unchecked -> ShowTasks=false`.
- Search не должен менять выбранность сам по себе; он только меняет видимый набор пунктов.
- Summary text не является persisted state; он каждый раз вычисляется из выбранных фильтров.
- Summary порядок: текущий порядок списка фильтров.
- Summary формат: selected emoji separated by spaces, then `+N` for overflow, for example `🚀 🧰 ❌ +3`.
- Summary must always keep overflow count visible when not all selected items fit.
- Empty-emoji service filters are excluded from selected emoji summary and searchable popup list unless explicitly required by existing filter semantics.

## 8. Точки интеграции и триггеры
- Триггер открытия: click/tap, `Enter`, or `Space` по компоненту в закрытом состоянии.
- Триггер search mode: повторный click/tap по компоненту при открытом dropdown, `Enter`, `F2`, or printable character while popup is open.
- Триггер поиска: изменение локального search text внутри компонента.
- Триггер выбора: изменение checkbox `IsChecked`.
- Триггер reset: существующие команды reset фильтров в ViewModel; новая реализация не должна менять их.
- Триггер обновления списка: существующий пересчет `EmojiFilters` / `EmojiExcludeFilters`.

## 9. Изменения модели данных / состояния
- Новое calculated-поле `EmojiFilter.SearchText`.
- Локальное transient state компонента: `IsDropDownOpen`, `IsSearchFocused`, `SearchText`.
- Persisted state не меняется.
- Хранилище задач не меняется.

## 10. Миграция / Rollout / Rollback
- Первый запуск: migration не нужна.
- Обратная совместимость: старые задачи и фильтры работают через те же collections.
- Rollback: вернуть старые `ComboBox` в `MainControl.axaml` и `GraphControl.axaml`, удалить новый dropdown-контрол и `SearchText`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В закрытом состоянии компонент показывает selected emoji summary в формате `🚀 🧰 ❌ +N`.
  - Summary использует текущий порядок списка фильтров.
  - Служебные empty-emoji фильтры не попадают в selected emoji summary и searchable popup list.
  - Первый клик открывает полный прокручиваемый popup/dropdown checkbox-список без фильтрации.
  - В полном списке пользователь может прокручивать, включать и выключать фильтры checkbox-ом.
  - Checkbox-click не закрывает dropdown.
  - Повторный клик по компоненту при открытом списке фокусирует пустой text field.
  - Пока search field пустой, список остается полным.
  - После ввода части названия emoji/tag открытый checkbox-список фильтруется.
  - Если совпадений нет, показывается warning и полный список остается доступным.
  - `Esc` очищает search, затем закрывает component popup, затем может закрыть parent filter panel.
  - Keyboard flow позволяет открыть список, войти в search, перейти к item, переключить checkbox и закрыть popup.
  - Popup anchored к компоненту, не клипается parent flyout и остается в usable viewport на narrow viewport.
  - Пользователь может включить найденный фильтр checkbox-ом.
  - Пользователь может выключить выбранный фильтр checkbox-ом без reset.
  - Пользователь может выбрать несколько include и несколько exclude emoji-фильтров.
  - Include и exclude доступны отдельными компонентами, layout остается как сейчас.
  - Exclude-компонент визуально отличим.
  - Закрытие dropdown очищает search text и возвращает summary.
  - Reset-фильтры продолжают очищать выбранные emoji-фильтры.
- Какие тесты добавить/изменить:
  - Добавить UI-тест summary order/overflow, открытия полного popup-списка, повторного click-to-search, фильтрации, no-results warning, checkbox toggle without close и include/exclude behavior.
  - Добавить keyboard UI-тест для открытия, focus search, filtering, checkbox toggle и `Esc` behavior.
  - Добавить UI-тест popup placement/clipping внутри текущего filter flyout на narrow viewport.
  - Добавить UI-тест `FilterFlyout_EmojiFilters_LongList_RemainsBoundedOnNarrowViewport` с 20+ emoji-фильтрами.
  - Добавить или обновить Roadmap UI-тест, который открывает Roadmap filter flyout и проверяет тот же компонент.
  - Прогнать весь `MainControlFilterToolbarResponsiveUiTests`.
  - Прогнать `MainControlResetFiltersUiTests`.
- Characterization tests / contract checks:
  - Проверить, что checkbox item использует текущий `EmojiFilter.ShowTasks`.
  - Проверить, что первый click показывает полный список, включая элементы, которые не совпадают с будущим search text.
  - Проверить, что повторный click фокусирует empty search input.
  - Проверить, что поиск по подстроке title/sort text сокращает список до ожидаемого пункта.
  - Проверить, что search text сам по себе не меняет `ShowTasks`.
  - Проверить, что component popup не закрывает parent filter panel при взаимодействии внутри popup.
  - Проверить, что service empty-emoji filters не попадают в searchable popup list и summary.
- Visual acceptance:
  - В узкой панели фильтров поля не должны вылезать за ширину flyout.
  - Summary должен помещаться в одну строку поля с trimming/ellipsis and visible `+N` overflow when needed.
  - Dropdown list должен иметь bounded height и внутренний scroll, чтобы длинный список emoji-фильтров не вытеснял остальные группы фильтров.
  - Сценарий с 20+ emoji-фильтрами на narrow viewport должен оставаться в bounds flyout.
  - Popup должен быть anchored к компоненту, визуально доступен, не клипаться родительским flyout и не выходить за usable viewport.
  - Include/exclude должны быть читаемы и не перекрывать другие группы фильтров.
- UI video evidence:
  - Не применимо, video harness для этого flow не задан.
  - Fallback: screenshot через `Unlimotion.ReadmeMedia --ux-review filter-toolbar`; если harness зависает, явно указать это в отчете.
- Базовые замеры до/после для performance tradeoff:
  - Не применимо, изменение small UI и работает на существующей in-memory коллекции.
- Команды для проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj --no-restore /nodeReuse:false -p:UseSharedCompilation=false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/FilterFlyout_EmojiFilters_SupportSummaryDropdownAndSearch" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/FilterFlyout_EmojiFilters_KeyboardFlow_Works" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/FilterFlyout_EmojiFilters_PopupStaysVisibleInNarrowViewport" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/FilterFlyout_EmojiFilters_LongList_RemainsBoundedOnNarrowViewport" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/RoadmapFilterFlyout_EmojiFilters_SupportSummaryDropdownAndSearch" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*" --output Detailed
```

- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted UI tests fail, исправить до завершения EXEC.
  - Если desktop screenshot harness зависает два раза, остановить harness, не оставлять процессы и сообщить fallback.

## 12. Риски и edge cases
- Риск: внутренние биндинги контрола унаследуют DataContext родительской ViewModel вместо самого control.
  - Смягчение: использовать binding через `$parent[views:EmojiFilterMultiSelectSearchBox]` или явные compiled-binding контракты контрола.
- Риск: nested popup закроет parent filter flyout или потеряет focus.
  - Смягчение: явно реализовать и протестировать nested popup / parent flyout contract.
- Риск: search mode начнет открываться первым кликом и сломает привычный сценарий "открыть список и проставить галочки".
  - Смягчение: явно реализовать click sequence: первый клик открывает полный список, повторный клик фокусирует empty search input.
- Риск: summary выбранных фильтров не помещается в поле.
  - Смягчение: показывать selected emoji в порядке списка и `+N` для overflow.
- Риск: длинный dropdown вытеснит остальные фильтры в narrow flyout.
  - Смягчение: bounded dropdown height, внутренний scroll и UI-тест с 20+ emoji-фильтрами.
- Риск: новый контрол шире старого `ComboBox` и ломает narrow flyout.
  - Смягчение: фиксированная ширина с min-width, responsive UI-тесты и screenshot fallback.
- Риск: keyboard behavior расходится с pointer/touch behavior.
  - Смягчение: отдельный keyboard UI-тест.
- Риск: existing reset не сбрасывает новый UI.
  - Смягчение: новый UI меняет те же `ShowTasks`, reset-tests остаются актуальными.
- Риск: Roadmap flyout получает новый контрол, но остается без тестового покрытия.
  - Смягчение: добавить Roadmap UI coverage. В этой спеки Roadmap остается in scope, значит coverage обязателен.

## 13. План выполнения
1. Дождаться утверждения spec.
2. Добавить calculated `EmojiFilter.SearchText`.
3. Добавить `EmojiFilterMultiSelectSearchBox` с summary/search field, nested popup и checkbox dropdown-list.
4. Заменить старые emoji `ComboBox` в `MainControl.axaml` и `GraphControl.axaml`.
5. Добавить EN/RU placeholder/empty-state strings.
6. Добавить UI-тест summary, первого открытия полного popup-списка, повторного click-to-search, filtering/no-results, checkbox toggle without close и include/exclude behavior.
7. Добавить keyboard UI-тест.
8. Добавить popup placement/clipping UI-тест.
9. Добавить long-list narrow viewport UI-тест.
10. Добавить Roadmap UI coverage для нового компонента.
11. Сравнить текущий uncommitted draft EXEC с утвержденной spec: если draft не соответствует popup/dropdown/checkbox/search контракту, переписать его, а не продолжать как есть.
12. Запустить build и targeted UI tests.
13. Попробовать получить visual evidence; при зависании harness остановить процессы и описать fallback.
14. Сделать self-review diff и финально отчитаться.

## 14. Открытые вопросы
- Нет открытых вопросов.
- Зафиксированные ответы интервью:
  - `1b`: отдельный popup/dropdown как обычный `ComboBox`.
  - `2a`: при повторном клике поле пустое с placeholder, список остается полным до ввода.
  - `3a`: при закрытии search text всегда очищается, поле снова показывает summary.
  - `4b`: summary формат `🚀 🧰 ❌ +3`.
  - `5a`: порядок выбранных элементов как в текущем списке фильтров.
  - `6c`: если нет совпадений, оставить полный список и показать warning.
  - `7a`: keyboard behavior важен.
  - `8a`: checkbox-click не закрывает dropdown.
  - `9a`: include/exclude остаются двумя одинаковыми компонентами, exclude с красным акцентом.
  - `10a`: на узкой ширине список ограничен по высоте и скроллится внутри компонента.

## 15. Соответствие профилю
- Профиль: desktop UI-facing feature
- Выполненные требования профиля:
  - Spec-first до утвержденного EXEC.
  - UI-тесты обязательны.
  - Visual evidence запланирован через existing harness/fallback.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml` | Новый searchable multi-select dropdown | Reusable UI для include/exclude emoji filters без потери dropdown/checkbox UX |
| `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml.cs` | Summary/open/search state, nested popup behavior, filtering, keyboard и exclude style class | Показывать selected emoji summary, открывать полный список, фильтровать после повторного клика |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | `EmojiFilter.SearchText` | Текстовый контракт поиска |
| `src/Unlimotion/Views/MainControl.axaml` | Замена emoji `ComboBox` | Multi-select search dropdown в task filter flyout |
| `src/Unlimotion/Views/GraphControl.axaml` | Замена emoji `ComboBox` | Multi-select search dropdown в Roadmap filter flyout |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | EN placeholders/empty state | Локализация |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | RU placeholders/empty state | Локализация |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | Новые UI-тесты | Приемка summary/dropdown/search/keyboard/toggle behavior |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Include emoji filter | `ComboBox` со списком checkbox-пунктов | Compact summary field + popup/dropdown с теми же checkbox-пунктами |
| Exclude emoji filter | Красный `ComboBox` со списком checkbox-пунктов | Compact summary field + popup/dropdown с exclude style и теми же checkbox-пунктами |
| Searchability | Только визуальный выбор | Первый клик открывает полный список; повторный клик фокусирует empty search; ввод фильтрует checkbox-list |
| Reset | Существующий reset `ShowTasks` | Без изменения |
| Точечное снятие фильтра | Checkbox в dropdown | Checkbox в dropdown |
| Индикация выбранного | Отображение выбранного `ComboBox` ограничено | Selected emoji summary + `+N` overflow |
| Нет совпадений | Не применимо | Warning + полный список остается доступным |
| Keyboard | Нативное поведение `ComboBox` | Явный keyboard contract |

## 18. Альтернативы и компромиссы
- Вариант: оставить `ComboBox` и включить editable/search mode.
- Плюсы: меньше нового UI-кода.
- Минусы: хуже контроль summary text, two-click search mode, nested popup behavior, multi-select checkbox behavior и automation ids.
- Почему выбранное решение лучше в контексте этой задачи:
  - Отдельный searchable multi-select dropdown сохраняет привычный dropdown/checkbox UX, добавляет selected emoji summary и дает стабильный контракт для тестов.

- Вариант: inline searchable checklist в панели фильтров.
- Плюсы: все элементы сразу видны без dropdown.
- Минусы: занимает больше места, может вытеснить остальные фильтры, отличается от текущего клика по `ComboBox`.
- Почему не выбран:
  - Пользователь явно выбрал отдельный popup/dropdown как обычный `ComboBox`.

- Вариант: `AutoCompleteBox`, где выбор suggestion сразу активирует фильтр и очищает поле.
- Плюсы: быстро реализуется и хорошо ищет по тексту.
- Минусы: теряет старую checkbox/toggle-семантику, хуже показывает активные фильтры и усложняет точечное снятие.
- Почему не выбран:
  - Пользователь явно потребовал отсутствие регрессии и checkbox-list.

- Вариант: отдельное окно/tag picker.
- Плюсы: больше пространства для длинного списка.
- Минусы: больше scope и navigation complexity.
- Почему не выбран:
  - Задача small, пользователь просит заменить конкретный control.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и non-goals описаны |
| B. Качество дизайна | 6-10 | PASS | Responsibility split, state, behavior, nested popup, keyboard и rollback заданы |
| C. Безопасность изменений | 11-13 | PASS | UI tests, visual acceptance, риски и план выполнения описаны |
| D. Проверяемость | 14-16 | PASS | Открытые вопросы закрыты интервью |
| E. Готовность к автономной реализации | 17-19 | PASS | Можно запрашивать approval, EXEC только после фразы подтверждения |
| F. Соответствие профилю | 20 | PASS | Журнал действий заведен |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope narrow и non-goals заданы |
| 2. Понимание текущего состояния | 5 | Указаны текущие файлы, модели и сохранение checkbox/dropdown UX |
| 3. Конкретность целевого дизайна | 5 | Summary, first click, second click search, no-results, keyboard и nested popup behavior описаны |
| 4. Безопасность (миграция, откат) | 5 | Нет persisted migration, rollback описан |
| 5. Тестируемость | 5 | Команды и тестовые контракты заданы |
| 6. Готовность к автономной реализации | 5 | Открытые вопросы закрыты, approval-gate сохранен |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению после approval

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-08-emoji-filter-text-search.md`, instruction stack, selected profile, open questions, planned changed files
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: PASS
  - Contract pass: PASS
  - Adversarial risk pass: PASS
  - Re-review after fixes / Fix and re-review: выполнено после интервью
  - Stop decision: ждать подтверждения
- Evidence inspected:
  - Текущая структура `MainControl.axaml`, `GraphControl.axaml`, `EmojiFilter`.
- Depth checklist:
  - Scope drift / unrelated changes: риск есть из-за уже выполненного преждевременного EXEC; после approval draft должен быть перепроверен и переписан, если не соответствует текущему контракту.
  - Acceptance criteria: заданы.
  - Validation evidence: команды заданы.
  - Unsupported claims: нет.
  - Regression / edge case: reset, responsive, summary fitting, nested popup behavior, keyboard, checkbox toggle и Roadmap coverage описаны.
  - Comments/docs/changelog: changelog не требуется.
  - Hidden contract change: persisted state не меняется.
  - Manual-review challenge: проверить, не закрывает ли nested popup родительский filter flyout и не ломает ли keyboard navigation.
- No-findings justification: spec готова к approval; EXEC не должен продолжаться без подтверждения.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | process | EXEC был начат до spec approval | Остановиться до approval; после approval перепроверить draft на соответствие spec | accepted-risk |
| MEDIUM | UX | Первичный design терял checkbox/toggle UX | Перевести решение на multi-select dropdown с checkbox-list | fixed |
| MEDIUM | UX | Inline checklist мог раздувать filter flyout | Вернуть dropdown model и bounded list | fixed |
| MEDIUM | integration | Nested popup contract был не определен | Зафиксировать parent flyout, focus, scroll и Esc behavior | fixed |
| MEDIUM | integration | Popup placement/clipping был недостаточно проверяем | Добавить explicit visual/test contract для narrow viewport | fixed |
| LOW | UX | Empty-emoji summary fallback не был явно принят | Исключить service empty-emoji filters из summary/list и описать future fallback | fixed |
| LOW | process | Stop rule можно было прочитать как остановку после approval | Заменить на остановку до approval | fixed |

- Fixed before continuing: spec обновлена по итогам интервью.
- Checks rerun: не применимо для SPEC.
- Needs human: подтвердить spec фразой `Спеку подтверждаю`.
- Residual risks / follow-ups: текущие uncommitted code changes являются draft EXEC и требуют переписывания под этот контракт после approval.

### Post-EXEC Review
- Статус: PASS после runtime regression fix и designer feedback fix
- Scope reviewed: `EmojiFilterMultiSelectSearchBox`, замены emoji-фильтров в task/Roadmap flyout, `EmojiFilter.SearchText`, EN/RU resources, responsive UI tests, generated screenshot artifact.
- Decision: можно передавать результат пользователю; blocking regressions не обнаружены.
- Review passes:
  - Scope/Evidence pass: PASS
  - Contract pass: PASS
  - Adversarial risk pass: PASS
  - Re-review after fixes / Fix and re-review: выполнено после обнаружения search-field reclick issue, nested popup overlay regression и дизайнерских замечаний по popup/search/summary/list density
  - Stop decision: финальный отчет
- Evidence inspected:
  - Before fix: `dotnet src/Unlimotion.Test/bin/Release/net10.0/Unlimotion.Test.dll --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/FilterFlyout_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing" --output Detailed` -> FAIL на `ShouldUseOverlayLayer == true`.
  - After fix: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/FilterFlyout_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing" --output Detailed` -> PASS, 1/1.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --output Detailed` -> PASS, 12/12.
  - Latest designer feedback fix: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*"` -> PASS, 13/13.
  - `dotnet src/Unlimotion.Test/bin/Release/net10.0/Unlimotion.Test.dll --output Detailed --timeout 8m` -> PASS, 463/463.
  - `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release --no-restore -p:UseSharedCompilation=false /nr:false` -> PASS.
  - Latest designer feedback fix: `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release --no-restore -p:UseSharedCompilation=false /nr:false` -> PASS, 36 warnings, 0 errors.
  - `git diff --check` -> PASS, только LF->CRLF warnings.
  - Latest visual evidence attempt: `dotnet run --no-build --project tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review filter-toolbar --language ru --output-root artifacts\emoji-filter-search\designer-feedback --no-build-before-launch` -> TIMEOUT after 124s; leftover `Unlimotion.ReadmeMedia`, `Unlimotion.Desktop` and `Unlimotion.Test` processes were stopped; partial screenshot shows closed task list, not open popup.
  - `dotnet run --no-build --project tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review filter-toolbar --language ru --output-root artifacts\emoji-filter-search\after --no-build-before-launch` -> FAIL на existing harness assertion `Filter panel 'AllTasksFilterPanel' was not opened`.
  - Частичный screenshot: `artifacts/emoji-filter-search/after/task-narrow-alltasks.png`.
- Depth checklist:
  - Scope drift / unrelated changes: source changes are limited to approved spec scope; `chat-artifacts/` remains untracked local evidence.
  - Acceptance criteria: summary, first click full list, second click search, filtering, no-results fallback, checkbox toggle without close, keyboard flow, narrow popup bounds and Roadmap coverage are covered by UI tests.
  - Validation evidence: targeted UI test, full TUnit assembly run and desktop build passed.
  - Unsupported claims: no claim of open flyout screenshot because ReadmeMedia failed to open panel.
  - Regression / edge case: service empty-emoji filters are excluded from summary/list; reset behavior continues through unchanged `ShowTasks` state model.
  - Comments/docs/changelog: no changelog needed; spec journal updated.
  - Hidden contract change: persisted state and task model are unchanged.
  - Manual-review challenge: nested popup parent-flyout behavior and narrow viewport placement are asserted in headless UI tests.
- No-blocking-findings justification: tests cover the approved UX contract and direct review found no remaining contract mismatch.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | visual evidence | ReadmeMedia `filter-toolbar` capture saved a closed-state screenshot, then failed opening `AllTasksFilterPanel`; open popup evidence is covered by headless UI tests | Report screenshot limitation; do not change product code for harness issue | accepted-risk |

- Fixed before final report: repeated click inside already-active search field now preserves the current search text and only restores focus/caret; nested dropdown no longer forces overlay layer inside filter flyouts; designer feedback fixed outside-click close, title search, popup placement, vertical alignment, All toggle, no-results background and list density.
- Checks rerun: desktop build, `git diff --check`, targeted TUnit, full filter UI class, full built TUnit assembly; after designer feedback fix latest filter UI class rerun passed 13/13.
- Validation evidence: latest filter UI tests 13/13, latest desktop build PASS, latest `git diff --check` PASS; earlier full `Unlimotion.Test` 463/463 remains historical runtime-regression evidence; current open-popup screenshot unavailable because `ReadmeMedia` timed out.
- Unrelated changes: `chat-artifacts/` is untracked local evidence, not source scope.
- Needs human: Нет.
- Residual risks / follow-ups: open-flyout screenshot remains unavailable because ReadmeMedia did not open `AllTasksFilterPanel`; headless UI tests are the current evidence for that state.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | process correction | 0.95 | Подтверждение пользователя и ответы на открытые вопросы | Ждать решения пользователя | Да | Да, пользователь указал нарушение spec-first процесса | Создана spec, EXEC остановлен | `specs/2026-06-08-emoji-filter-text-search.md` |
| SPEC | no-regression revision | 0.98 | Только approval | Ждать `Спеку подтверждаю` | Да | Да, пользователь уточнил no-regression contract | Спека изменена с one-shot autocomplete на no-regression checkbox/toggle | `specs/2026-06-08-emoji-filter-text-search.md` |
| SPEC | summary-dropdown revision | 0.99 | Только approval | Ждать `Спеку подтверждаю` | Да | Да, пользователь уточнил summary, full dropdown и second-click search behavior | Спека изменена на compact multi-select dropdown с count/summary и поиском после повторного клика | `specs/2026-06-08-emoji-filter-text-search.md` |
| SPEC | interview answers revision | 0.99 | Только approval | Ждать `Спеку подтверждаю` | Да | Да, пользователь ответил `1b,2a,3a,4b,5a,6c,7a,8a,9a,10a` | Спека уточнена по nested popup, summary, no-results, keyboard и bounded scroll | `specs/2026-06-08-emoji-filter-text-search.md` |
| SPEC | review fixes | 0.99 | Только approval | Ждать `Спеку подтверждаю` | Да | Да, пользователь попросил исправить findings review | Исправлены popup placement/clipping, empty-emoji behavior и stop rule | `specs/2026-06-08-emoji-filter-text-search.md` |
| EXEC | implementation | 0.90 | Финальная сборка, diff-check и visual evidence | Запустить финальные проверки | Нет | Да, пользователь подтвердил spec | Реализован reusable multi-select dropdown, замены в task/Roadmap flyout, локализация и UI-тесты | `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml`, `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml.cs`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.ViewModel/Resources/Strings.resx`, `src/Unlimotion.ViewModel/Resources/Strings.ru.resx`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` |
| EXEC | targeted validation | 0.94 | Visual evidence и финальный self-review | Прогнать сборку, diff-check, попытаться получить screenshot | Нет | Нет | Целевой UI-набор `MainControlFilterToolbarResponsiveUiTests` прошел 12/12 | `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` |
| EXEC | initial final validation | 0.93 | На тот момент не хватало healthy NuGet environment для свежего full/test build | Передать итог пользователю | Нет | Нет | Desktop build и direct targeted TUnit assembly passed; ReadmeMedia дал partial closed screenshot и упал на open-panel assertion; позднее runtime regression fix обновил validation до full TUnit 463/463 | `artifacts/emoji-filter-search/after/task-narrow-alltasks.png`, `specs/2026-06-08-emoji-filter-text-search.md` |
| EXEC | runtime regression fix | 0.97 | Только manual confirmation in real app if desired | Передать итог пользователю | Нет | Да, пользователь сообщил crash при клике на панель | Зафиксирован failing regression assertion на `ShouldUseOverlayLayer == true`, затем popup переведен в non-overlay режим для nested flyout; full TUnit assembly прошел 463/463 | `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml.cs`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`, `specs/2026-06-08-emoji-filter-text-search.md` |
| EXEC | designer feedback fix | 0.97 | Только manual confirmation in real app if desired | Передать итог пользователю | Нет | Да, пользователь перечислил 8 UX/regression замечаний со screenshot | Исправлены light-dismiss outside click, search by emoji/name, popup alignment, summary vertical alignment, All bulk toggle, no-results background and compact row density; UI class прошел 13/13, desktop build PASS, diff-check PASS; `ReadmeMedia` timed out on visual evidence and was cleaned up | `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml`, `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml.cs`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`, `specs/2026-06-08-emoji-filter-text-search.md` |
