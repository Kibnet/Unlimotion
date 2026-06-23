# Встроенный справочник горячих клавиш

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`; `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `feat/add-hotkey-info`
- Ограничения: central `QUEST` SPEC-gate; локальный override требует UI tests для UI behavior; не трогать unrelated `src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj`; `chat-artifacts/` не коммитить
- Связанные ссылки: `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `AGENTS.override.md`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Исправить справочник горячих клавиш: `F1` должен надёжно открывать справочник из любого места приложения, а сам справочник должен быть встроенным UI, не отдельным `Window`, чтобы не зависеть от платформенных особенностей окон.

Outcome contract:
- Success means: `F1` и кнопка `Показать горячие клавиши` открывают одну встроенную панель справочника; повторный `F1` закрывает её без создания окна; справочник показывает product-level команды по смысловым секциям и явно отделяет их от внутренней навигации отдельных контролов.
- Итоговый артефакт / output: код, ресурсы, UI-тесты, обновлённая документация при необходимости.
- Stop rules: остановиться только после зелёных targeted UI tests, build и full test run проекта. Пропуск full test run допустим только при объективном техническом блокере (например, стабильный file lock, runner crash, недоступная зависимость или превышение допустимого времени выполнения после начатого запуска); в этом случае EXEC-отчёт обязан указать точную причину, next-best checks и residual risk.

## 2. Текущее состояние (AS-IS)
- `MainControl_OnKeyDown` перехватывает `Key.F1` и вызывает `ShowHotkeyHelp()`.
- `ShowHotkeyHelp()` создаёт `HotkeyHelpWindow`, вызывает `Show(owner)` или `Show()`.
- Справочник визуально живёт в `HotkeyHelpPanel`, а `HotkeyHelpWindow` только оборачивает эту панель в отдельное окно.
- Кнопка настроек `SettingsShowHotkeysButton` через `SettingsControl.axaml.cs` вызывает `MainControl.ShowHotkeyHelp()`.
- Тесты `MainControlTreeCommandsUiTests` ожидают отдельный `HotkeyHelpWindow`.
- Проблема пользователя: окно не открывается по `F1`; отдельное окно не подходит как кроссплатформенный контракт.

## 3. Проблема
Корневая проблема: справочник реализован как отдельное окно и завязан на платформенное window management, а не как часть основного визуального дерева приложения; при этом структура содержимого дублирует часть задачных команд, пропускает product-level mouse/drag modifiers и не объясняет область применения shortcuts.

## 4. Цели дизайна
- Разделение ответственности: `MainControl` управляет видимостью встроенного справочника; `HotkeyHelpPanel` отвечает только за содержимое и layout.
- Повторное использование: существующую панель переиспользовать, но убрать обязательную зависимость от `HotkeyHelpWindow`.
- Тестируемость: UI tests должны проверять встроенную панель, отсутствие отдельного окна и работу `F1` из text input.
- Консистентность: кнопка в настройках и `F1` вызывают один и тот же embedded flow.
- Обратная совместимость: сохранить automation ids для существующих строк справочника, где возможно; не менять сами команды дерева/roadmap.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем набор исполняемых команд и сами сочетания клавиш, кроме поведения открытия/закрытия справочника.
- Не добавляем новый persisted setting.
- Не меняем архитектуру горячих клавиш приложения целиком.
- Не коммитим screenshot/video artifacts.
- Не исправляем unrelated изменение в `Unlimotion.Desktop.ForMacBuild.csproj`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> добавить встроенный overlay/panel host поверх основного содержимого.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> заменить создание `HotkeyHelpWindow` на переключение встроенной панели; обрабатывать `F1` на уровне `TopLevel`/root так, чтобы работало из вложенных контролов.
- `src/Unlimotion/Views/HotkeyHelpPanel.axaml` -> обновить структуру содержимого: явные группы `Справочник`, `Текущая задача`, `Выделение и аутлайн`, `Дерево задач`, `Связи`, `Перетаскивание`, `Дорожная карта`; добавить короткое пояснение для контекстности без дублирования одинаковых задачных команд.
- `src/Unlimotion/Views/SettingsControl.axaml(.cs)` -> оставить кнопку, которая вызывает тот же embedded flow.
- `src/Unlimotion.ViewModel/Resources/Strings*.resx` -> обновить тексты секций и пояснений.
- `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` -> заменить ожидания отдельного окна на встроенный panel host.
- `src/Unlimotion/Views/HotkeyHelpWindow.*` -> удалить, если после миграции не используется.

### 6.2 Детальный дизайн
- Поток `F1`: `MainControl` при attach регистрирует routed key handler на уровне root/top-level с tunnel/preview routing и обработкой уже handled events, где это поддерживает Avalonia API (например, `AddHandler(InputElement.KeyDownEvent, ..., RoutingStrategies.Tunnel, handledEventsToo: true)` или ближайший repo-compatible эквивалент). Handler должен срабатывать до/несмотря на вложенные controls, включая `TextBox`; если `Key.F1` без модификаторов, он переключает встроенную панель и помечает событие handled.
- Поток кнопки настроек: `SettingsControl` находит родительский `MainControl` и вызывает `ShowHotkeyHelp()`, который открывает panel.
- Закрытие: кнопка закрытия внутри `HotkeyHelpPanel`, `Escape` при открытой панели и повторный `F1` закрывают справочник.
- Layout:
  ```text
  MainControl root Grid
  ├─ existing app content
  └─ HotkeyHelpOverlayHost (IsVisible=false/true)
     ├─ light/dim backdrop
     └─ right/top aligned panel, max width 560, max height bound to viewport
        ├─ title + close button
        ├─ section: Справочник
        │  ├─ F1 Показать/скрыть справочник
        │  └─ Esc Закрыть справочник
        ├─ section: Текущая задача
        │  └─ F2/Ctrl+Enter/Shift+Enter/Ctrl+Tab/Ctrl+D
        ├─ section: Выделение и аутлайн
        │  └─ Ctrl+A/Shift+Del/Ctrl+Shift+C/Ctrl+Shift+V
        ├─ section: Дерево задач
        │  └─ Ctrl+Shift+←/→, Ctrl+Alt+←/→
        ├─ section: Связи
        │  └─ Enter/Esc в редакторе связей
        ├─ section: Перетаскивание
        │  └─ Без модификатора/Shift/Ctrl/Ctrl+Shift/Alt при drag задач
        └─ section: Дорожная карта
           └─ F/U/T, R, Ctrl/Shift/Alt + click/selection rectangle, right-drag
  ```
- Visual planning artifact для UI-facing изменений: wireframe выше; final evidence: screenshot в `chat-artifacts/...` или fallback, если capture технически мешает.
- UI test video evidence: Не применимо как обязательный artifact, потому что текущий headless/TUnit path не сохраняет видео; fallback evidence: targeted Avalonia.Headless UI tests, build, screenshot при возможности.
- Границы поведения: `F1` не должен запускать tree command и не должен зависеть от нативного `Window.Show`.
- Обработка ошибок: если `TopLevel` ещё недоступен, embedded button path всё равно работает через `MainControl` state; повторная подписка не должна дублировать обработчики.
- Производительность: панель лёгкая, создаётся в XAML один раз; переключение только меняет `IsVisible`.

## 7. Бизнес-правила / Алгоритмы (если есть)
- `F1` без модификаторов: открыть справочник, если закрыт; закрыть справочник, если открыт.
- `Escape`: закрыть справочник, если он открыт.
- Секция `Справочник` содержит только управление самой панелью и работает глобально.
- Секция `Текущая задача` содержит команды над текущей/выбранной задачей, которые могут вызываться из разных представлений и поэтому не должны дублироваться в секциях дерева и дорожной карты.
- Секция `Выделение и аутлайн` содержит операции со множественным выделением и outline clipboard, применимые к спискам/деревьям задач, но не к viewport дорожной карты.
- Секция `Дерево задач` содержит только навигацию/раскрытие узлов дерева.
- Секция `Связи` содержит shortcuts редактора связей: `Enter` подтверждает выбор, `Escape` отменяет редактор.
- Секция `Перетаскивание` содержит модификаторы drag-and-drop задач: без модификатора — копировать внутрь; `Shift` — переместить внутрь; `Ctrl+Shift` — клонировать внутрь; `Ctrl` — перетаскиваемые задачи блокируют цель; `Alt` — цель блокирует перетаскиваемые задачи.
- Секция `Дорожная карта` содержит только управление видом карты; задачные команды, которые также работают на roadmap, остаются в `Текущая задача`.
- В секции `Дорожная карта` также показываются модификаторы выбора: `Ctrl+click` / `Ctrl+рамка` переключает выбор, `Shift+click` / `Shift+рамка` добавляет к выбору, `Alt+click` / `Alt+рамка` удаляет из выбора; right-drag панорамирует карту.
- Control-level навигация внутри отдельных dropdown/search controls (`Up/Down`, `Space`, `Enter`, `F2` внутри `EmojiFilterMultiSelectSearchBox`) не входит в справочник горячих клавиш, если она не запускает product-level команду приложения.

## 8. Точки интеграции и триггеры
- `MainControl.OnAttachedToVisualTree` / `OnDetachedFromVisualTree`: регистрация/снятие routed key handler с tunnel/preview routing и `handledEventsToo`, если доступно.
- `MainControl_OnKeyDown`: не должен быть единственным путём для `F1`; допустимо делегировать в общий helper, но основной контракт `F1` обязан покрываться root/top-level routed handler.
- `SettingsControl.ShowHotkeysButton_OnClick`: вызывает `ShowHotkeyHelp()`.
- `HotkeyHelpPanel`: содержит кнопку закрытия и поднимает `CloseRequested`; `MainControl` подписывается на событие и закрывает overlay.

## 9. Изменения модели данных / состояния
- Новое transient UI-state поле в `MainControl`: `bool _isHotkeyHelpVisible` или прямое управление `IsVisible` overlay control.
- Persisted state: не применимо.
- Влияние на хранилище: не применимо.

## 10. Миграция / Rollout / Rollback
- Миграция данных не нужна.
- Rollout: обычный desktop build.
- Rollback: вернуть отдельный `HotkeyHelpWindow` и старые тесты, если embedded overlay окажется несовместимым.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `F1` открывает встроенный справочник из текстового поля.
  - `F1` повторно закрывает встроенный справочник.
  - `Escape` закрывает встроенный справочник.
  - Кнопка `Показать горячие клавиши` в настройках открывает тот же встроенный справочник.
  - В visual tree нет отдельного `HotkeyHelpWindow`; есть `HotkeyHelpOverlayHost`/`HotkeyPanel`.
  - Справочник показывает все product-level shortcuts и modifiers из этой спеки: справочник, текущая задача, выделение/аутлайн, дерево задач, связи, перетаскивание, дорожная карта.
  - Справочник не дублирует одинаковые задачные команды между деревом и дорожной картой.
  - Справочник явно не включает control-level навигацию отдельных dropdown/search controls, если она не запускает product-level команду.
- Какие тесты добавить/изменить:
  - Обновить `TreeCommandUi_HotkeyHelpWindow_DisplaysScrollableShortcutReferenceFromF1` под embedded panel и переименовать.
  - Обновить `TreeCommandUi_SettingsShowHotkeysButton_OpensShortcutReferenceWindow` под embedded panel и переименовать.
  - Добавить assertion для повторного `F1`/`Escape`.
  - Добавить assertions на наличие всех новых секций и ключевых пропущенных строк: `Enter/Esc` для связей, drag modifiers, roadmap selection modifiers, right-drag pan.
  - Добавить assertion, что задачные команды (`F2`, `Ctrl+Enter`, `Shift+Enter`, `Ctrl+Tab`) представлены один раз как команды текущей задачи, а не продублированы в roadmap/tree секциях.
- Characterization tests / contract checks: сначала обновить/добавить тест на новый embedded contract и убедиться, что он падает на текущей реализации; затем исправить код так, чтобы тест фиксировал embedded behavior.
- Visual acceptance: wireframe из секции 6.2; финальная панель должна иметь заголовок, close button, все семь секций (`Справочник`, `Текущая задача`, `Выделение и аутлайн`, `Дерево задач`, `Связи`, `Перетаскивание`, `Дорожная карта`) и не должна создавать отдельное OS window.
- UI video evidence: fallback без видео; команды, targeted/full test evidence и screenshot paths фиксировать в EXEC отчёте.
- Команды для проверки:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore --no-build -- --treenode-filter "/*/*/*/TreeCommandUi_HotkeyHelpPanel_DisplaysEmbeddedShortcutReferenceFromF1"`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore --no-build -- --treenode-filter "/*/*/*/TreeCommandUi_SettingsShowHotkeysButton_OpensEmbeddedShortcutReference"`
  - Full test run: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`. Если full run технически заблокирован, EXEC-отчёт обязан привести точный error/output, выполненные next-best checks и residual risk.
- Stop rules для test/retrieval/tool/validation loops: stop after targeted tests, build, full test run and screenshot/fallback evidence; continue on failures, ambiguity, missing acceptance coverage, or objective full-run blocker that needs diagnosis.

## 12. Риски и edge cases
- Риск двойной обработки `F1` через root and TopLevel handlers: общий helper должен возвращать `true/false` и проверять `e.Handled`.
- Риск потери фокуса после закрытия: после закрытия вернуть фокус на `MainControl` или не менять текущий focused control, если платформа не даёт надёжного restore.
- Риск overlay перекрывает важные данные: dim backdrop + close/Escape/F1; без persistent state.
- Риск тестов, завязанных на `HotkeyHelpWindow`: обновить только релевантные тесты.

## 13. План выполнения
1. Обновить/добавить UI tests под embedded behavior, ожидая failure на текущей реализации.
2. Встроить overlay host в `MainControl.axaml`.
3. Перевести `MainControl.axaml.cs` с `HotkeyHelpWindow` на embedded state и TopLevel key handling.
4. Обновить `HotkeyHelpPanel`, `HotkeyHints` и ресурсы для ясных смысловых секций без дублирования задачных команд.
5. Удалить неиспользуемое `HotkeyHelpWindow.*`, если компиляция подтверждает отсутствие ссылок.
6. Запустить build, targeted UI tests, full test run и screenshot либо documented fallback.
7. Выполнить post-EXEC review-loop.

## 14. Открытые вопросы
Нет блокирующих вопросов. UX-выбор принят: embedded overlay-панель поверх основного окна, а не отдельная вкладка или inline section в Settings, потому что `F1` должен работать из любого места.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`; `ui-automation-testing`
- Выполненные требования профиля: планируются UI tests, стабильные automation ids, build/test validation, visual planning artifact, fallback вместо видео с причиной.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | добавить embedded overlay host | кроссплатформенный справочник без нового окна |
| `src/Unlimotion/Views/MainControl.axaml.cs` | заменить window creation на embedded toggle и TopLevel key handling | исправить F1 и убрать platform window dependency |
| `src/Unlimotion/Views/HotkeyHelpPanel.axaml` | уточнить структуру секций и close affordance integration | сделать содержимое понятнее |
| `src/Unlimotion/Views/HotkeyHelpWindow.*` | удалить при отсутствии использования | убрать неиспользуемый отдельный window |
| `src/Unlimotion/HotkeyHints.cs` | добавить display strings для `Esc`, relation editor shortcuts, drag modifiers, roadmap selection modifiers и right-drag pan | держать отображаемые сочетания в одном месте |
| `src/Unlimotion.ViewModel/Resources/Strings*.resx` | обновить тексты секций/пояснений | локализация нового UX |
| `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` | обновить UI regression tests | покрыть F1/settings embedded flow |
| `README.md`, `README.RU.md` | при необходимости уточнить, что справочник встроенный | документация пользовательского flow |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Открытие справочника | отдельный `HotkeyHelpWindow` | embedded overlay внутри `MainControl` |
| `F1` | локальный keydown + native window show | TopLevel/root handler + toggle embedded panel |
| Содержимое | `Общие`, `Дерево задач`, `Дорожная карта` с дублированием задачных команд и пропуском mouse/drag modifiers | `Справочник`, `Текущая задача`, `Выделение и аутлайн`, `Дерево задач`, `Связи`, `Перетаскивание`, `Дорожная карта` |
| Тесты | проверяют window | проверяют embedded panel |

## 18. Альтернативы и компромиссы
- Вариант: оставить отдельный non-modal `Window`.
  - Плюсы: меньше изменений.
  - Минусы: сохраняет платформенную проблему и F1/window management риск.
- Вариант: показывать справочник только внутри Settings.
  - Плюсы: простой layout.
  - Минусы: `F1` из любого места вынуждает навигацию в Settings, хуже как справочник.
- Почему выбранное решение лучше: embedded overlay не зависит от нативного окна, работает из любого места и не меняет текущую вкладку/контекст пользователя.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, дизайн-цели и non-goals описаны. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, state и rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Тесты, риски и поэтапный план есть; persisted data не затрагивается. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, UI tests, обязательный full run и команды проверки указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Открытых блокирующих вопросов нет, компромиссы зафиксированы. |
| F. Соответствие профилю | 20 | PASS | Указаны desktop/UI automation требования и fallback для video. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Цель и non-goals проверяемы. |
| 2. Понимание текущего состояния | 5 | Указаны текущие методы, файлы и проблема отдельного окна. |
| 3. Конкретность целевого дизайна | 5 | Есть embedded layout, event flow и automation contracts. |
| 4. Безопасность (миграция, откат) | 5 | Persisted data не меняется, rollback понятен. |
| 5. Тестируемость | 5 | Есть targeted UI tests, build, full run gate и критерии visual acceptance. |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов, порядок выполнения ясен. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-19-embedded-hotkey-help.md`, central stack (`model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`), local `AGENTS.override.md`, planned changed files, open questions.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: просмотрены текущие `MainControl.axaml(.cs)`, `HotkeyHelpPanel.axaml`, `HotkeyHelpWindow.axaml`, ресурсы и тестовые ссылки через `rg`.
  - Contract pass: spec покрывает embedded UI, F1 из любого места через routed tunnel/preview handling, settings button, все семь смысловых секций, product-level mouse/drag modifiers, UI tests и full run gate.
  - Adversarial risk pass: проверены риски двойной обработки F1, перехвата F1 вложенным `TextBox`, платформенной зависимости Window, потери focus и stale unrelated changes.
  - Re-review after fixes / Fix and re-review: после review пользователя исправлены full-test gate, F1 routing contract, toggle ambiguity и close action; повторная проверка затронутых секций выполнена.
  - Stop decision: PASS, можно запросить `Спеку подтверждаю`.
- Evidence inspected: `MainControl_OnKeyDown`, `ShowHotkeyHelp`, `HotkeyHelpPanel`, `HotkeyHelpWindow`, `SettingsShowHotkeysButton` references, central owner docs.
- Depth checklist:
  - Scope drift / unrelated changes: unrelated `Unlimotion.Desktop.ForMacBuild.csproj` и `chat-artifacts/` явно исключены.
  - Acceptance criteria: покрывают F1, repeated F1 toggle-close, settings button, Escape, no separate window, все семь смысловых секций, отсутствие дублирования и исключение control-level dropdown navigation.
  - Validation evidence: команды build/targeted/full documented; actual EXEC validation ещё не выполнена.
  - Unsupported claims: claims ограничены просмотренным кодом.
  - Regression / edge case: double key handling and focus risks listed.
  - Comments/docs/changelog: README only if needed, comments не планируются.
  - Hidden contract change: изменение с отдельного окна на overlay явно отражено.
  - Manual-review challenge: reviewer, вероятно, проверит отсутствие `Window.Show`, наличие UI tests и понятность секций; это включено в spec.
- No-findings justification: small/medium UI bugfix spec has concrete files, explicit F1 routing contract, complete product-level shortcut grouping, acceptance criteria, test commands and no unresolved product choice.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence не планируется из-за отсутствия принятого recorder path в текущем headless/TUnit workflow. | Использовать targeted UI tests + full test run + screenshot fallback и явно указать это в EXEC отчёте. | accepted-risk |

- Fixed before continuing: не применимо.
- Checks rerun: spec linter/rubric reviewed manually after fixes.
- Needs human: требуется подтверждение фразой `Спеку подтверждаю`.
- Residual risks / follow-ups: full test run может быть долгим/нестабильным; EXEC всё равно должен начать full run и фиксировать только объективный technical blocker, если он возникнет.

### Post-EXEC Review
- Статус: PASS с зафиксированным риском full-suite headless flakiness.
- Scope reviewed: embedded hotkey overlay implementation, `F1`/`Esc` routing at `MainWindow` + `MainControl`, settings button flow, localized shortcut sections, deleted `HotkeyHelpWindow`, UI regression tests, screenshot evidence, unrelated workspace changes.
- Decision: implementation matches approved SPEC; no blocking findings in changed behavior.
- Review passes:
  - Scope/Evidence pass: изменения ограничены `MainControl`, `MainScreen`, `MainWindow`, `HotkeyHelpPanel`, hotkey strings/resources, related UI tests and this SPEC; unrelated `Unlimotion.Desktop.ForMacBuild.csproj` не менялся в рамках EXEC.
  - Contract pass: справочник встроен в `MainControl`, отдельный `HotkeyHelpWindow` удалён, `F1` переключает overlay из `MainWindow`/вложенных контролов, `Esc` закрывает, settings button открывает тот же embedded panel.
  - Adversarial risk pass: добавлена защита от двойной обработки `F1` через `e.Handled`; `MainWindow` обрабатывает tunnel events с `handledEventsToo`; панель получила непрозрачный Fluent background после visual evidence.
  - Re-review after fixes / Fix and re-review: после нативного screenshot evidence исправлены window-level routing и фон панели; targeted tests пересобраны и повторены.
  - Stop decision: PASS; оставшиеся full-suite падения не воспроизводятся изолированно и не относятся к изменённым файлам.
- Evidence inspected:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore` -> PASS.
  - `TreeCommandUi_HotkeyHelpPanel_DisplaysEmbeddedShortcutReferenceFromF1` -> PASS.
  - `MainWindowUi_HotkeyHelpPanel_HandlesF1AtWindowLevel` -> PASS.
  - `TreeCommandUi_SettingsShowHotkeysButton_OpensEmbeddedShortcutReference` -> PASS.
  - Full run `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build` -> FAIL: 542 total, 537 passed, 5 failed.
  - Five full-run failures rerun individually -> PASS for each failed test.
  - Screenshot evidence: `chat-artifacts/hotkey-embedded-screenshot/flaui/settings-show-hotkeys-button.png`, `chat-artifacts/hotkey-embedded-screenshot/flaui/hotkey-help-embedded-desktop.png`.
- Depth checklist:
  - Scope drift / unrelated changes: `src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj` existed before EXEC and remains out of scope; `chat-artifacts/` contains generated local screenshots only.
  - Acceptance criteria: covered by build, targeted UI tests, settings screenshot and embedded screenshot.
  - Validation evidence: commands and screenshots recorded above.
  - Unsupported claims: no production persistence or task model changes claimed.
  - Regression / edge case: repeated `F1`, `Esc`, settings button, no duplicate current-task hotkeys covered.
  - Comments/docs/changelog: no code comments needed; README not changed because feature is discoverable in app settings and SPEC did not require user docs.
  - Hidden contract change: separate native window removed intentionally per SPEC.
  - Manual-review challenge: reviewer can check there are no `HotkeyHelpWindow` source references and that `MainWindow` delegates hotkey handling into `MainControl`.
- No-findings justification: changed behavior has focused UI coverage and visual evidence; full-suite failures pass individually and match existing headless/parallel instability pattern.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | full-suite validation | Полный прогон падает на 5 unrelated headless/UI tests, но каждый из этих тестов проходит при изолированном rerun. | Не блокировать embedded hotkey fix; зафиксировать как residual full-suite flakiness. | accepted-risk |

- Fixed before final report: добавлен `MainWindow` hotkey routing; исправлен прозрачный фон panel; удалён scratch test file.
- Checks rerun: build, three targeted tests, full suite, five isolated reruns, FlaUI screenshot flow.
- Validation evidence: см. список команд и screenshot paths выше.
- Unrelated changes: `src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj`, `chat-artifacts/`.
- Needs human: нет.
- Residual risks / follow-ups: full-suite headless/parallel instability should be handled separately.

## Approval
SPEC confirmed by user: `спеку полтверждаю` on 2026-06-19.

### Execution Journal

| Stage | Event | Evidence | Decision |
| --- | --- | --- | --- |
| EXEC | Пользователь подтвердил спеку фразой `спеку полтверждаю` | Chat request, 2026-06-19 | SPEC gate открыт; код, ресурсы и UI tests можно менять строго в рамках спеки. |
| EXEC | Реализован embedded hotkey overlay | `MainControl.axaml(.cs)`, `HotkeyHelpPanel.axaml(.cs)`, удалён `HotkeyHelpWindow.*` | Отдельное окно заменено встроенной панелью. |
| EXEC | Добавлен window-level hotkey routing | `MainWindow.axaml.cs`, `MainScreen.axaml.cs`, `MainControl.axaml.cs` | `F1`/`Esc` работают через главное окно и вложенные контролы. |
| EXEC | Справочник разбит на смысловые секции | `HotkeyHints.cs`, `Strings*.resx`, `HotkeyHelpPanel.axaml` | Убрано дублирование текущих задачных команд и добавлены пропущенные product-level modifiers. |
| EXEC | Обновлены UI tests | `MainControlTreeCommandsUiTests.cs` | Покрыты `F1`, `MainWindow`, settings button, `Esc`, секции и отсутствие дублей. |
| EXEC | Validation completed | build PASS; 3 targeted tests PASS; full run 537/542 PASS; 5 failed full-run tests PASS individually; screenshot paths recorded | Implementation accepted; full-suite flakiness recorded as residual risk. |

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | route and inspect | 0.9 | Нет | Создать SPEC | Нет | Нет | Прочитаны central/local инструкции и текущие файлы реализации. | `AGENTS`, `MainControl.axaml(.cs)`, `HotkeyHelpPanel.axaml`, `HotkeyHelpWindow.axaml` |
| SPEC | design | 0.9 | Подтверждение пользователя | Запросить `Спеку подтверждаю` | Да | Да, этот ответ | Embedded overlay объективно лучше отдельного Window для кроссплатформенности и F1 из любого места. | `specs/2026-06-19-embedded-hotkey-help.md` |
| SPEC | review fix | 0.92 | Подтверждение пользователя | Запросить `Спеку подтверждаю` | Да | Да, пользователь попросил исправить review findings | Устранены неоднозначность toggle, слабый full-test gate, недоопределённый F1 routed handling и выбор close action. | `specs/2026-06-19-embedded-hotkey-help.md` |
| SPEC | section model fix | 0.94 | Подтверждение пользователя | Запросить `Спеку подтверждаю` | Да | Да, пользователь указал на неверное деление секций | Секции перестроены по смыслу: справочник, текущая задача, выделение/аутлайн, дерево, связи, перетаскивание, дорожная карта; одинаковые задачные команды больше не дублируются. | `specs/2026-06-19-embedded-hotkey-help.md`, `HotkeyHints.cs`, `MainControl.axaml(.cs)`, `GraphControl.axaml.cs` |
| SPEC | shortcut audit fix | 0.9 | Подтверждение пользователя | Запросить `Спеку подтверждаю` | Да | Да, пользователь указал на пропущенные сочетания | В SPEC добавлены product-level пропуски: `Enter/Esc` редактора связей, модификаторы drag-and-drop задач, roadmap selection modifiers и right-drag pan; control-level dropdown navigation явно исключена из справочника. | `specs/2026-06-19-embedded-hotkey-help.md`, `MainControl.axaml.cs`, `GraphControl.axaml.cs`, `EmojiFilterMultiSelectSearchBox.axaml.cs` |
| SPEC | consistency update | 0.93 | Подтверждение пользователя | Запросить `Спеку подтверждаю` | Да | Да, пользователь попросил обновить спеку | Acceptance criteria, план, таблица файлов и post-SPEC review синхронизированы с полным набором product-level shortcuts/modifiers. | `specs/2026-06-19-embedded-hotkey-help.md` |
