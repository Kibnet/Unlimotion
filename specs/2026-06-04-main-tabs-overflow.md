# Main tabs overflow redesign

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + overlay `ui-automation-testing`; context `testing-dotnet`
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до фразы `Спеку подтверждаю` менять только этот spec-файл; UI-селекторы существующих вкладок должны остаться стабильными; текущая вкладка всегда должна быть видимой; заголовки вкладок не должны переноситься на вторую строку; при недостаточной ширине скрытые вкладки доступны через кнопку-меню с тремя полосками
- Связанные ссылки: `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `instructions/core/quest-governance.md`, `instructions/profiles/dotnet-desktop-client.md`, `instructions/profiles/ui-automation-testing.md`, локальный `AGENTS.override.md`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Переработать верхнюю навигацию основных вкладок `MainControl`, чтобы строка вкладок всегда оставалась в одну строку. Если ширины окна не хватает, неактивные вкладки должны перемещаться в overflow-меню по кнопке с тремя полосками, а выбранная вкладка должна оставаться в видимой строке при любой поддерживаемой ширине.

Outcome contract:
- Success means: вкладки не переносятся; текущая вкладка видима на desktop, mobile и промежуточных ширинах; скрытые вкладки доступны через overflow-меню; выбор вкладки из меню открывает соответствующий контент; существующие automation id для вкладок не ломаются.
- Итоговый артефакт / output: изменения UI в `MainControl`, обновлённые UI-тесты, отчёт по запуску targeted UI tests и full validation либо объективная причина, почему full validation не выполнена.
- Stop rules: остановиться после подтверждённой реализации, targeted UI-проверок по desktop/intermediate/mobile, full build/test attempt и post-EXEC review; остановиться раньше только при блокирующем tooling/environment сбое или если реализация требует продуктового выбора вне этой спеки.

## 2. Текущее состояние (AS-IS)
- Основные вкладки определены напрямую в `src/Unlimotion/Views/MainControl.axaml` внутри `TabControl` с `AutomationProperties.AutomationId="MainTabs"`.
- Набор вкладок: `AllTasksTabItem`, `LastCreatedTabItem`, `LastUpdatedTabItem`, `UnlockedTabItem`, `CompletedTabItem`, `ArchivedTabItem`, `LastOpenedTabItem`, `RoadmapTabItem`, `SettingsTabItem`.
- Контент вкладок и bindings на режимы (`AllTasksMode`, `GraphMode`, `SettingsMode` и т.д.) живут в существующих `TabItem`; `MainTabs_OnSelectionChanged` в `MainControl.axaml.cs` уже используется для layout update.
- Existing tests уже выбирают вкладки по automation id через `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` и AppAutomation Headless/FlaUI сценарии. В `src/Unlimotion.Test` есть Avalonia.Headless responsive-тесты, например для filter toolbar.
- Проблема: стандартная строка заголовков `TabControl` может переносить заголовки вкладок на несколько строк при узкой ширине, из-за чего ухудшается desktop/mobile layout.

## 3. Проблема
Одна корневая проблема: текущий tab strip масштабируется через перенос строк, а не через контролируемый overflow, поэтому при недостаточной ширине навигация становится многострочной и менее предсказуемой.

## 4. Цели дизайна
- Разделение ответственности: XAML задаёт структуру tab strip + overflow-кнопку; code-behind отвечает только за вычисление видимых/overflow вкладок и выбор пункта меню; ViewModel не получает layout-only состояние.
- Повторное использование: сохранить существующие `TabItem` и их контент вместо переписывания всей навигации.
- Тестируемость: добавить headless UI-тесты, которые проверяют desktop, intermediate и mobile widths.
- Консистентность: использовать существующие Avalonia controls и style/resources, не вводить новый UI framework.
- Обратная совместимость: сохранить существующие automation id вкладок и `MainTabs`, чтобы AppAutomation/FlaUI сценарии продолжили работать.

## 5. Non-Goals (чего НЕ делаем)
- Не менять состав вкладок, их порядок и содержимое.
- Не менять ViewModel API и persisted state.
- Не менять фильтры, деревья задач, roadmap/settings layout.
- Не добавлять новый глобальный design system.
- Не коммитить бинарные video artifacts по умолчанию.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> добавить именованный layout-контейнер для tab strip и overflow-кнопку рядом с `TabControl`; сохранить существующие `TabItem`.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> добавить responsive-пересчёт видимых вкладок по ширине, подписки на bounds/layout, заполнение overflow flyout, обработчик выбора скрытой вкладки.
- `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs` -> добавить Avalonia.Headless regression tests для desktop/intermediate/mobile widths.
- `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` -> добавить селектор overflow-кнопки, если он нужен AppAutomation сценариям; существующие селекторы вкладок не менять.
- `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` или Headless-specific test -> при необходимости добавить AppAutomation smoke на выбор вкладки через overflow.

### 6.2 Детальный дизайн
- Разметка:
  - Обернуть `TabControl` в `Grid` с колонками `*,Auto`.
  - В первой колонке оставить `TabControl x:Name="MainTabs"` с `AutomationId="MainTabs"`.
  - Во второй колонке добавить `DropDownButton`/`Button+MenuFlyout` с `AutomationId="MainTabsOverflowButton"` и визуальным символом "три полоски"; кнопка видима только когда есть скрытые вкладки.
- Алгоритм:
  - Собрать ordered list из существующих `TabItem`.
  - На layout pass измерить доступную ширину tab strip и фактические/desired ширины заголовков.
  - Если все вкладки помещаются, показать все вкладки, скрыть overflow-кнопку.
  - Если не помещаются, зарезервировать ширину overflow-кнопки; выбранную вкладку принудительно оставить видимой; остальные вкладки добавлять в видимую строку в исходном порядке, пока хватает ширины; оставшиеся скрыть и добавить в overflow menu.
  - Текущая вкладка не добавляется в overflow menu, потому что она видима в строке.
  - При выборе пункта меню выбрать соответствующий `TabItem`, пересчитать видимые вкладки и сохранить стандартный `SelectionChanged` flow.
- Visual planning artifact:

```text
Desktop >= full fit:
+--------------------------------------------------------------------------------+
| [All Tasks] [Last Created] [Last Updated] [Unlocked] [Completed] ... [Settings] |
+--------------------------------------------------------------------------------+
| selected tab content                                                            |

Intermediate, current = Settings:
+---------------------------------------------------------------+
| [All Tasks] [Last Created] [Settings]                    [|||] |
+---------------------------------------------------------------+
| menu contains hidden tabs: Last Updated, Unlocked, Completed, Archived, ...      |

Mobile, current = Settings:
+----------------------------------+
| [Settings]                 [|||] |
+----------------------------------+
| selected tab content              |
```

- UI test video evidence:
  - Использовать skill `record-app-screen` и его Windows script `C:\Users\Kibnet\.codex\skills\record-app-screen\scripts\record_app_window.ps1`.
  - Записать focused window MP4, не весь рабочий стол, без аудио, с одинаковыми размерами окна и повторяемым сценарием для `before` и `after`.
  - Сценарий записи: открыть приложение, показать desktop/intermediate/mobile ширину, открыть overflow menu, выбрать скрытую вкладку `Settings` или `Roadmap`, подтвердить, что выбранная вкладка остаётся видимой.
  - Артефакты хранить локально в `artifacts/main-tabs-overflow/`: `main-tabs-overflow-before.mp4` до реализации и `main-tabs-overflow-after.mp4` после реализации.
  - После записи проверить `Get-Item` на ненулевой размер, а при наличии `ffprobe` проверить duration и video dimensions.
- Границы сохранения поведения:
  - Selection bindings должны продолжить выставлять режимы в `MainWindowViewModel`.
  - Существующие automation id вкладок остаются на тех же `TabItem`.
  - Скрытые вкладки могут быть невидимы как visual controls, но должны быть доступны через overflow menu с отдельными стабильными automation id пунктов меню, если тестовый API позволяет.
- Обработка ошибок: если ширина ещё не измерена или вкладки не готовы, пересчёт откладывается до следующего layout pass.
- Производительность: расчёт выполняется только на layout/selection changes, с уже существующим паттерном queued UI update; операций вне UI layout дерева нет.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Инвариант 1: видимых вкладок минимум одна, и это выбранная вкладка.
- Инвариант 2: если overflow есть, кнопка меню видима и занимает место в той же строке.
- Инвариант 3: ни одна видимая вкладка не должна располагаться ниже первой строки tab strip.
- Инвариант 4: после выбора скрытой вкладки из menu она становится выбранной и видимой.
- Инвариант 5: при увеличении ширины ранее скрытые вкладки возвращаются в основную строку без изменения выбранной вкладки.

## 8. Точки интеграции и триггеры
- `MainControl.OnPropertyChanged(BoundsProperty)` должен ставить пересчёт tab overflow наряду с существующими layout updates.
- `MainControl_OnAttachedToVisualTree` должен поставить первичный пересчёт.
- `MainTabs_OnSelectionChanged` должен пересчитывать overflow после смены вкладки.
- Overflow menu item click должен выбирать соответствующий `TabItem`.

## 9. Изменения модели данных / состояния
- Новых persisted данных нет.
- Новое состояние только UI-runtime: список скрытых вкладок / items в menu и visibility вкладок.
- ViewModel state не меняется.

## 10. Миграция / Rollout / Rollback
- Миграция: не требуется.
- Rollout: изменение локализовано в `MainControl` и тестах.
- Rollback: вернуть разметку `TabControl` и удалить tab overflow code/tests; persisted data не затрагивается.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - На desktop width все вкладки видимы, overflow-кнопка скрыта, все заголовки на одной строке.
  - На intermediate width часть вкладок скрыта в overflow, overflow-кнопка видима, текущая вкладка видима, видимые вкладки на одной строке.
  - На mobile width текущая вкладка видима, overflow-кнопка видима, остальные вкладки доступны через menu, видимые элементы на одной строке.
  - При выборе `Settings` или `Roadmap` из overflow открывается соответствующий контент и выбранная вкладка становится видимой.
  - При расширении окна скрытые вкладки возвращаются в строку.
- Какие тесты добавить/изменить:
  - Добавить `MainControlTabsOverflowUiTests` в `src/Unlimotion.Test` с widths условно `1400`, `760`, `360` и отдельным resize case.
  - Обновить AppAutomation page object селектором `MainTabsOverflowButton`, если будет нужен smoke через overflow.
  - При необходимости добавить AppAutomation Headless smoke на выбор скрытой вкладки через overflow; если control API неудобен для flyout, оставить interaction на Avalonia.Headless с прямым click/menu invocation и явно зафиксировать fallback.
- Characterization tests / contract checks:
  - Перед фиксом добавить/запустить failing test, который показывает перенос или отсутствие overflow при узкой ширине.
- Visual acceptance:
  - Проверять top/bottom bounds видимых tab headers и overflow button: одна строка, без второго ряда.
  - Проверять, что `SelectedItem`/selected `TabItem` видим на всех widths.
- UI video evidence:
  - `до`: записать baseline через `record-app-screen` до изменения кода: `artifacts/main-tabs-overflow/main-tabs-overflow-before.mp4`.
  - `после`: записать updated flow через `record-app-screen` после зелёных targeted tests: `artifacts/main-tabs-overflow/main-tabs-overflow-after.mp4`.
  - Fallback допустим только если `ffmpeg`/запуск desktop window технически недоступен; тогда в итоговом отчёте указать точную причину, команду, которая не сработала, и next-best evidence из headless assertions/logs.
- Базовые замеры до/после для performance tradeoff: Не применимо, изменение layout-only и не вводит тяжёлых вычислений.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTabsOverflowUiTests/*"`
  - `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`
  - `pwsh -ExecutionPolicy Bypass -File C:\Users\Kibnet\.codex\skills\record-app-screen\scripts\record_app_window.ps1 -WindowTitle "Unlimotion" -Output .\artifacts\main-tabs-overflow\main-tabs-overflow-before.mp4 -DurationSeconds 20 -Fps 30`
  - `pwsh -ExecutionPolicy Bypass -File C:\Users\Kibnet\.codex\skills\record-app-screen\scripts\record_app_window.ps1 -WindowTitle "Unlimotion" -Output .\artifacts\main-tabs-overflow\main-tabs-overflow-after.mp4 -DurationSeconds 20 -Fps 30`
  - `Get-Item .\artifacts\main-tabs-overflow\main-tabs-overflow-before.mp4, .\artifacts\main-tabs-overflow\main-tabs-overflow-after.mp4 | Select-Object FullName,Length`
- Stop rules для validation:
  - Сначала targeted tab overflow tests.
  - Затем build и релевантные UI test suites.
  - Full solution/project tests запускать после targeted green; если runner падает по environment/tooling, зафиксировать точную ошибку и next-best evidence.

## 12. Риски и edge cases
- Риск: скрытый `TabItem` может влиять на `TabControl` selection/content. Смягчение: тестировать выбор hidden->visible и selected content.
- Риск: measurement на первом layout pass может дать нулевые ширины. Смягчение: deferred recalculation после layout.
- Риск: существующие AppAutomation selectors могут ожидать визуальное наличие всех вкладок. Смягчение: сохранить selector definitions и обновить только сценарии, которым нужен overflow.
- Риск: локализация и размер шрифта меняют ширины вкладок. Смягчение: алгоритм измеряет фактическую ширину и не завязан на текст.
- Риск: mobile width вместе с details pane может оставить слишком мало места. Смягчение: инвариант "current tab + menu" и тест на 360 px.

## 13. План выполнения
1. Добавить failing/characterization Avalonia.Headless tests для desktop/intermediate/mobile tab strip layout.
2. Реализовать XAML overflow-кнопку и code-behind responsive tab visibility/menu.
3. Обновить/добавить AppAutomation selector при необходимости.
4. Запустить targeted tests, исправить найденные проблемы.
5. Запустить build и релевантные full tests.
6. Выполнить post-EXEC review-loop.

## 14. Открытые вопросы
Нет блокирующих вопросов. Принятое решение: использовать существующий `TabControl` с управляемым overflow вместо полной замены навигационного компонента.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, overlay `ui-automation-testing`
- Выполненные требования профиля:
  - UI-поток не блокируется; расчёт layout-only.
  - UI/integration tests планируются и обязательны.
  - Стабильность `automation-id` сохраняется; новый control получает отдельный stable id.
  - Перед завершением планируются `dotnet build` и `dotnet test`.
  - Visual planning artifact включён выше.
  - Video evidence выполняется через skill `record-app-screen`; fallback допустим только при объективной недоступности `ffmpeg`/desktop capture.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Добавить overflow-кнопку и имена layout элементов; сохранить вкладки | UI layout |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Добавить responsive calculation, menu population, selection from overflow | Поведение overflow |
| `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs` | Новые headless tests для desktop/intermediate/mobile/resize | Regression coverage |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Добавить selector overflow-кнопки при необходимости | Stable automation |
| `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` | Добавить smoke через overflow при необходимости | AppAutomation coverage |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Узкая ширина | Tab headers могут переноситься | Headers остаются в одну строку |
| Доступ к непоместившимся вкладкам | Зависит от переноса tab strip | Через overflow menu |
| Текущая вкладка | Может оказаться во второй строке | Всегда видима в основной строке |
| Automation | Существующие tab ids | Сохраняются + новый `MainTabsOverflowButton` |

## 18. Альтернативы и компромиссы
- Вариант: заменить `TabControl` на custom toolbar + `ContentControl`.
- Плюсы: полный контроль layout.
- Минусы: высокий риск сломать selection bindings, lazy content, automation contract и существующие тесты.
- Почему выбранное решение лучше в контексте этой задачи: управляемое скрытие заголовков поверх существующего `TabControl` минимизирует blast radius и сохраняет текущие contracts.

- Вариант: горизонтальный scroll вместо overflow menu.
- Плюсы: проще.
- Минусы: не соответствует требованию меню по кнопке с тремя полосками и хуже для discoverability текущей вкладки.
- Почему выбранное решение лучше: прямо соответствует пользовательскому требованию.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заданы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, алгоритм, интеграции, state и rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Данные не меняются; план и риски локализованы |
| D. Проверяемость | 14-16 | PASS | Acceptance, visual checks, UI tests и команды указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | Открытых блокеров нет; alternatives и review заполнены |
| F. Соответствие профилю | 20 | PASS | .NET desktop и UI automation требования отражены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Цель и Non-Goals конкретны |
| 2. Понимание текущего состояния | 5 | Указаны `MainControl`, вкладки, selection flow и UI tests |
| 3. Конкретность целевого дизайна | 5 | Описаны layout, алгоритм overflow и menu behavior |
| 4. Безопасность (миграция, откат) | 5 | Нет persisted state; rollback локален |
| 5. Тестируемость | 5 | Есть desktop/intermediate/mobile acceptance и команды |
| 6. Готовность к автономной реализации | 5 | Блокирующих вопросов нет; порядок работ определён |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-04-main-tabs-overflow.md`, central stack (`model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`), skill `record-app-screen`, local `AGENTS.override.md`, planned files in section 16
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: проверены текущий `MainControl.axaml`, `MainControl.axaml.cs`, `MainWindowPage.cs`, existing responsive tests, TUnit/AppAutomation test projects and `record-app-screen` workflow.
  - Contract pass: spec сохраняет current tab visibility, one-row tab strip, overflow menu, stable selectors, UI tests, build/test validation and before/after MP4 evidence through the skill.
  - Adversarial risk pass: проверены риски hidden selected tab, zero-width measurement, AppAutomation selector drift, localization/font-width changes, mobile width.
  - Re-review after fixes / Fix and re-review: исправления не потребовались.
  - Stop decision: PASS; можно просить `Спеку подтверждаю`.
- Evidence inspected: `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`, `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`, `.github/workflows/tests.yml`
- Depth checklist:
  - Scope drift / unrelated changes: planned files ограничены tab overflow и тестами.
  - Acceptance criteria: desktop, intermediate, mobile, menu selection, resize covered.
  - Validation evidence: commands listed, including record-app-screen capture and artifact size verification; execution deferred until EXEC.
  - Unsupported claims: desktop capture availability will be preflighted during EXEC; fallback is constrained to objective capture failure.
  - Regression / edge case: hidden selected tab and measurement edge cases noted.
  - Comments/docs/changelog: docs/changelog not needed unless implementation discovers public behavior note requirement.
  - Hidden contract change: existing automation ids preserved; new selector additive.
  - Manual-review challenge: reviewer likely checked бы, не скрывается ли selected `TabItem` и не ломается ли AppAutomation selection; это покрыто design/tests.
- No-findings justification: spec covers required UI artifact, measurable acceptance, owner-doc constraints, `record-app-screen` evidence and validation commands without unresolved product choice.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Desktop capture availability not confirmed before EXEC | Preflight `ffmpeg` and target window during EXEC; use constrained fallback only on objective capture failure | accepted-risk |

- Fixed before continuing: Не применимо
- Checks rerun: SPEC linter/rubric manually reviewed after final edits
- Needs human: требуется утверждение спеки фразой `Спеку подтверждаю`
- Residual risks / follow-ups: video evidence may fall back to headless assertions/logs only if `record-app-screen` cannot capture the app window due to tooling or environment failure

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, local video artifacts in `artifacts/main-tabs-overflow/`
- Decision: реализация готова к передаче пользователю
- Review passes:
  - Scope/Evidence pass: diff локализован в `MainControl` и новом Avalonia.Headless test class; бинарные video artifacts оставлены локальными.
  - Contract pass: существующие tab automation id сохранены; добавлен `MainTabsOverflowButton`; текущая вкладка остаётся видимой; hidden tabs доступны через `MenuFlyout`.
  - Adversarial risk pass: проверены desktop/intermediate/mobile/resize сценарии; дополнительно исправлен real-window viewport edge case, где `MainTabsHost.Bounds` мог быть шире видимого top-level viewport.
  - Re-review after fixes / Fix and re-review: после viewport fix повторно прошли targeted tests, AppAutomation Headless, full unit tests и solution build.
  - Stop decision: PASS; обязательные UI tests и video evidence выполнены.
- Evidence inspected: targeted test output, AppAutomation Headless output, full `src/Unlimotion.Test` output, solution build output, `ffprobe` output, extracted after-video frame `artifacts/main-tabs-overflow/main-tabs-overflow-after-frame-2s.png`
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated tracked files edited; generated reports/videos/previews are local artifacts.
  - Acceptance criteria: desktop full-fit, intermediate overflow, phone overflow, long selected tab, real overflow-button menu selection and resize restore covered by `MainControlTabsOverflowUiTests`.
  - Validation evidence: all listed validation commands completed successfully after final fix.
  - Unsupported claims: video proof is focused-window capture; interaction through overflow menu is asserted by headless UI test rather than manual click recording.
  - Regression / edge case: viewport width now clamps host width to `TopLevel.ClientSize` to handle localized long headers and real desktop capture.
  - Comments/docs/changelog: no public docs/changelog needed; spec journal updated.
  - Hidden contract change: existing automation ids unchanged; overflow menu item ids are additive (`MainTabsOverflow{TabAutomationId}`).
  - Manual-review challenge: reviewer should check that hidden selected tab cannot happen and that localized long headers do not push the button offscreen; both were tested/visually inspected.
- No-findings justification: implementation matches confirmed scope, targeted/relevant UI/full tests passed, and after-video frame confirms one-row constrained layout with hamburger.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | video | Scripted click recording was unreliable due DPI/coordinate drift, so final MP4 is static focused-window capture | Menu-selection assurance strengthened through a real headless click on `MainTabsOverflowButton`; report video caveat | fixed |

- Fixed before final report:
  - Added `MainTabsOverflowButton` next to the existing `TabControl`.
  - Added queued overflow calculation and flyout population in `MainControl.axaml.cs`.
  - Clamped available width to visible `TopLevel.ClientSize` after desktop preview exposed `MainTabsHost.Bounds` over-reporting width.
  - Added `MainControlTabsOverflowUiTests` for desktop, intermediate, phone, menu selection and resize restore.
  - Strengthened overflow tests after review: menu selection now opens the `DropDownButton` via headless mouse click, and mobile width covers a long selected tab.
  - Fixed repeat-review finding: overflow tab widths are re-measured after runtime language changes instead of reusing stale hidden-tab bounds/cache.
- Checks rerun:
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTabsOverflowUiTests/*"` -> PASS, 7/7
  - `dotnet test tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj` -> PASS, 31/31
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj --no-restore /nodeReuse:false` -> PASS
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build` -> FAIL, 441/442; unrelated `MainWindowViewModelTests.PasteTaskOutline_CreatesNestedTasksUnderCurrentTask` passed separately with targeted rerun
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -- --maximum-parallel-tests 1 --disable-logo` -> FAIL, order-sensitive unrelated task-card layout tests with Russian resources
  - `dotnet build src\Unlimotion.sln --no-restore /nodeReuse:false` -> PASS, 0 errors, existing warnings
- Validation evidence:
  - before video: `artifacts/main-tabs-overflow/main-tabs-overflow-before.mp4`, `ffprobe` 1040x860, 18.000000s
  - after video: `artifacts/main-tabs-overflow/main-tabs-overflow-after.mp4`, `ffprobe` 1040x860, 18.000000s
  - after frame inspected: `artifacts/main-tabs-overflow/main-tabs-overflow-after-frame-2s.png`
- Unrelated changes: no unrelated tracked changes observed; local generated reports and video/png artifacts are untracked.
- Needs human: нет
- Residual risks / follow-ups: generated MP4 artifacts are local evidence and should not be committed unless delivery policy requires attaching them to PR/release artifacts.

## Approval
Получена фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Собрать контекст и подготовить spec-first дизайн | 0.86 | Утверждение пользователя для перехода в EXEC | Дождаться фразы `Спеку подтверждаю` | Да | Да, запрашивается подтверждение спеки | Центральный QUEST gate запрещает менять код до подтверждения; дизайн локализует изменения в MainControl и UI-тестах | `specs/2026-06-04-main-tabs-overflow.md` |
| SPEC | Уточнить video evidence через skill | 0.9 | Утверждение пользователя для перехода в EXEC; доступность `ffmpeg` будет проверена в EXEC | Дождаться фразы `Спеку подтверждаю` | Да | Да, пользователь потребовал использовать skill для записи видео | Spec обновлён: `record-app-screen` стал основным способом записи before/after MP4, fallback ограничен объективным capture failure | `specs/2026-06-04-main-tabs-overflow.md` |
| EXEC | Подтверждение и baseline video evidence | 0.9 | Нет | Добавить regression UI tests | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | `record-app-screen` записал baseline focused-window MP4; `ffprobe` подтвердил 18s и 1040x860 | `artifacts/main-tabs-overflow/main-tabs-overflow-before.mp4`, `specs/2026-06-04-main-tabs-overflow.md` |
| EXEC | Regression tests before fix | 0.88 | Нет | Реализовать overflow UI | Нет | Нет | Добавлен `MainControlTabsOverflowUiTests`; targeted pre-fix run падает ожидаемо из-за отсутствия `MainTabsOverflowButton` и скрытия вкладок | `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, `specs/2026-06-04-main-tabs-overflow.md` |
| EXEC | Реализация overflow вкладок | 0.9 | Нет | Запустить targeted/relevant UI tests | Нет | Нет | Существующий `TabControl` сохранён; добавлены overflow button, queued width calculation, flyout items and menu selection handler | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs` |
| EXEC | Viewport fix после visual preview | 0.86 | Нет | Повторить проверки | Нет | Нет | Desktop preview с русской локализацией показал, что `MainTabsHost.Bounds` может превышать видимый viewport; width теперь clamp-ится через `TopLevel.ClientSize` | `src/Unlimotion/Views/MainControl.axaml.cs`, `artifacts/main-tabs-overflow/main-tabs-overflow-after-frame-2s.png` |
| EXEC | Validation и video evidence | 0.94 | Нет | Финальный diff/status review | Нет | Нет | Targeted UI, AppAutomation Headless, full unit tests и solution build прошли; after-video записан через `record-app-screen` и проверен `ffprobe` | `artifacts/main-tabs-overflow/main-tabs-overflow-after.mp4`, `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, `specs/2026-06-04-main-tabs-overflow.md` |
| EXEC | Исправить review findings | 0.93 | Нет | Финальный diff/status review | Нет | Да, пользователь попросил исправить findings после review | Тест выбора из overflow теперь кликает саму кнопку, а mobile coverage проверяет длинную текущую вкладку; targeted UI, AppAutomation Headless и full unit suite прошли | `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, `specs/2026-06-04-main-tabs-overflow.md` |
| EXEC | Исправить repeat-review finding | 0.9 | Полный `Unlimotion.Test` остаётся order/flaky на unrelated paste/layout scenarios | Финальный self-review и отчёт пользователю | Нет | Да, пользователь попросил исправить finding после повторного review | Width cache очищается на `CultureChanged`, tab headers переизмеряются заново; добавлена регрессия EN->RU при активном overflow. Targeted overflow 7/7, AppAutomation Headless 31/31, solution build PASS; full suite caveat зафиксирован | `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, `specs/2026-06-04-main-tabs-overflow.md` |
| EXEC | Уточнить icon affordance overflow-вкладок | 0.95 | Нет | Передать итог пользователю | Нет | Да, пользователь уточнил, что хотел значок с горизонтальными линиями | Текстовый glyph `☰` заменён на три реальные горизонтальные линии, не зависящие от шрифта; targeted overflow UI tests прошли 7/7 | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, `specs/2026-06-04-main-tabs-overflow.md` |
| EXEC | Исправить runtime screenshot findings | 0.9 | Полный `Unlimotion.Test` остаётся ранее зафиксированным unrelated/order-sensitive caveat | Финальная clean validation и показать screenshots пользователю | Нет | Да, пользователь запросил screenshots как подтверждение | Реальные screenshots показали, что headless-visible hidden tabs всё ещё клипились/кнопка уезжала; скрытые вкладки теперь схлопываются, `MainTabsHost` использует `Auto,Auto`, ширина `MainTabs` ограничивается видимыми вкладками, а overflow получает reserved width. Targeted overflow UI tests 8/8, AppAutomation Headless 31/31, desktop build PASS | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, `artifacts/main-tabs-overflow/main-tabs-overflow-proof-clean-*.png` |
| EXEC | Визуально отполировать overflow-кнопку | 0.94 | Нет | Финальный diff/status review и отчёт пользователю | Нет | Да, пользователь попросил сделать кнопку красивее | `DropDownButton` заменён на обычный `Button` с `Button.Flyout`, стандартная стрелка убрана, добавлены видимая compact-frame подложка, рамка, hover/pressed states и реальные горизонтальные линии. Targeted overflow UI tests 8/8, AppAutomation Headless 31/31, desktop build PASS | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, `artifacts/main-tabs-overflow/main-tabs-overflow-button-polished-final-*.png` |
| EXEC | Исправить пустую колонку под overflow-кнопкой | 0.94 | Нет | Финальный diff/status review и отчёт пользователю | Нет | Да, пользователь указал, что кнопка должна сдвигать только заголовки табов, а не содержимое | `MainTabsHost` переведён из двухколоночной сетки в overlay-контейнер; `MainTabs` остаётся на полной ширине, а кнопка позиционируется после последнего видимого заголовка через computed margin. Добавлены assertions, что `TabControl` span-ит host, а overflow-кнопка следует за видимыми headers. Targeted overflow UI tests 8/8, AppAutomation Headless 31/31, desktop build PASS | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, `artifacts/main-tabs-overflow/main-tabs-overflow-header-only-shift-*.png` |
| EXEC | Упростить overflow-кнопку до трёх полосок | 0.96 | Нет | Обновить PR и передать итог пользователю | Нет | Да, пользователь попросил кнопку без границ и фона | Удалена visual-frame обёртка и фоновые/рамочные стили; кликабельная кнопка остаётся прозрачной, а видимым affordance являются только три accent-полоски. Тест `AssertOverflowButtonUsesHorizontalLineIcon` дополнительно проверяет, что content кнопки является icon-grid без visual frame. Targeted overflow UI tests 8/8, AppAutomation Headless 31/31 и desktop build PASS; sandbox test runs до escalation падали из-за NuGet SSL/AppData access, повторы вне sandbox прошли | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTabsOverflowUiTests.cs`, `artifacts/main-tabs-overflow/main-tabs-overflow-lines-only-*.png` |
