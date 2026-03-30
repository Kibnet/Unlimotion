# Стабилизация desktop UI при больших размерах шрифта

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client`
- Overlay profile: `ui-automation-testing`
- Контексты: `testing-dotnet`, `visual-feedback`
- Владелец: Unlimotion desktop UI
- Масштаб: `small`
- Ограничения:
  - Не менять модель настроек и формат сохранения `Appearance:FontSize`.
  - Не делать общий редизайн экранов и не менять состав вкладок.
  - Исправлять только деградации, подтверждённые screenshot-прогоном.
- Связанные файлы:
  - `src/Unlimotion.ViewModel/AppearanceSettings.cs`
  - `src/Unlimotion/App.axaml`
  - `src/Unlimotion/App.axaml.cs`
  - `src/Unlimotion/Views/MainControl.axaml`
  - `src/Unlimotion/Views/SearchControl/SearchControl.axaml`
  - `src/Unlimotion/Views/SearchControl/SearchControlStyles.axaml`
  - `src/Unlimotion/Views/SettingsControl.axaml`
  - `src/Unlimotion.Test/SettingsViewModelTests.cs`
  - `artifacts/font-ui-review/Program.cs`

## 1. Overview / Цель
Сделать desktop UI устойчивее к `FontSize=18` и `FontSize=24`, чтобы верхняя навигация, breadcrumb и toolbar фильтров не разваливались на seeded content.

## 2. Текущее состояние (AS-IS)
- Производные размеры для tab headers растут слишком агрессивно.
- Поиск встроен в общий `WrapPanel`, из-за чего переносится вместе с фильтрами и визуально ломает toolbar.
- Breadcrumb не ограничен по переполнению и плохо ведёт себя с длинным текстом.
- Размеры search input/clear button частично фиксированы и не масштабируются вместе с текстом.

## 3. Проблема
Настройка глобального размера шрифта работает, но при больших значениях ломает ключевые участки layout, что ухудшает читаемость и навигацию.

## 4. Цели дизайна
- Смягчить рост производных размеров без отказа от глобального font scaling.
- Зафиксировать положение поиска справа и дать фильтрам переноситься независимо.
- Сделать переполнение breadcrumb контролируемым.
- Сохранить изменения локальными и совместимыми с текущими binding/view model.

## 5. Non-Goals
- Не вводить новый navigation control или overflow-меню для вкладок.
- Не перерабатывать все `WrapPanel` в приложении.
- Не менять поведение поисковой логики или фильтров.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
| Компонент / файл | Ответственность |
| --- | --- |
| `AppearanceSettings.cs` | Нормализованные формулы производных UI размеров. |
| `App.axaml` / `App.axaml.cs` | Общие dynamic resources для tab/search/floating control размеров. |
| `MainControl.axaml` | Безопасный breadcrumb, менее агрессивные tab headers, toolbar layout с отдельной зоной для поиска. |
| `SearchControl.axaml` / `SearchControlStyles.axaml` | Масштабируемые размеры search input и clear button. |
| `SettingsViewModelTests.cs` | Regression-тесты на формулы размеров. |

### 6.2 Детальный дизайн
- `App` будет хранить отдельные resources для:
  - `AppTabFontSize`
  - `AppTabMinHeight`
  - `AppSearchControlHeight`
  - `AppSearchClearButtonSize`
  - `AppSearchBarMinWidth`
  - `AppFloatingControlMinHeight`
- `AppearanceSettings` получит функции для вычисления этих размеров с cap/минимумами.
- В `MainControl` toolbar табов с поиском будет построен как `Grid`:
  - слева `WrapPanel` с фильтрами;
  - справа `SearchBar`.
- Breadcrumb будет однострочным с `CharacterEllipsis` и tooltip на полный текст.
- Вкладки останутся в текущем `TabControl`, но с менее агрессивным шрифтом и динамическим `MinHeight`.

## 7. Бизнес-правила / Алгоритмы
- `FontSize` по-прежнему остаётся единственным сохраняемым пользовательским параметром.
- Все остальные размеры вычисляются только от нормализованного `FontSize`.

## 8. Точки интеграции и триггеры
- Изменение `Settings.FontSize` -> `App.ApplyFontSize(...)` -> обновление dynamic resources.
- Повторный screenshot harness на `10/12/18/24`.

## 9. Изменения модели данных / состояния
- Persisted settings не меняются.
- Меняются только вычисляемые runtime resources и XAML layout.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - обновить formulas/resources
  - обновить XAML layout
  - прогнать unit tests + screenshot harness
- Rollback:
  - вернуть старые formulas/resources и исходный XAML toolbar layout

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
1. При `FontSize=24` tab headers не получают шрифт больше целевого cap.
2. Search input и clear button масштабируются от общих ресурсов, а не фиксированных чисел.
3. Toolbar в основных tab-сценариях держит поиск в отдельной правой зоне.
4. Повторный screenshot-прогон на `10/12/18/24` подтверждает отсутствие прежнего визуального развала в верхней части экрана.

### Команды для проверки
```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug
dotnet run --project artifacts/font-ui-review/FontUiReviewHarness.csproj
```

## 12. Риски и edge cases
- `TabControl` по-прежнему не имеет полноценного overflow UI; при экстремально узких окнах возможен остаточный стресс.
- Фикс ориентирован на реальные desktop размеры окна из harness, а не на mobile layout.

## 13. План выполнения
1. Обновить formulas/resources.
2. Перестроить toolbar и breadcrumb в `MainControl`.
3. Обновить search control sizing.
4. Добавить regression-тесты.
5. Прогнать `dotnet test`, build и screenshot harness.

## 14. Открытые вопросы
Блокирующих вопросов нет. Пользователь подтвердил выполнение после анализа screenshot-прогона 30 марта 2026.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - изменения локализованы в desktop UI
  - селектор `CurrentTaskTitleTextBox` не меняется
  - будут выполнены `dotnet build`, `dotnet test` и smoke screenshot-прогон

## 16. Альтернативы и компромиссы
- Вариант: внедрить отдельный overflow/tab menu.
- Плюсы:
  - полностью решает переполнение вкладок.
- Минусы:
  - заметно увеличивает объём UI-изменений.
- Почему выбранное решение лучше:
  - достаточно для подтверждённых проблем;
  - минимально меняет существующий UI-контракт.

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Persisted config не меняется, rollback локальный. |
| D. Проверяемость | 14-16 | PASS | Есть acceptance criteria и команды проверки. |
| E. Готовность к автономной реализации | 17-19 | PASS | Масштаб малый, блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | Спека соответствует `dotnet-desktop-client` и `ui-automation-testing`. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Чётко ограничено проблемными layout-участками. |
| 2. Понимание текущего состояния | 5 | Основано на screenshot-прогоне и конкретных симптомах. |
| 3. Конкретность целевого дизайна | 5 | Описаны формулы, ресурсы и layout-паттерн toolbar. |
| 4. Безопасность (миграция, откат) | 5 | Нет миграций данных, rollback прямой. |
| 5. Тестируемость | 5 | Есть unit tests, build и повторный visual smoke. |
| 6. Готовность к автономной реализации | 5 | Блокирующих неопределённостей нет. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

## Approval
Спека подтверждена пользователем 30 марта 2026.
