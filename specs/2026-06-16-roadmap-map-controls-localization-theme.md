# Локализация и theme-aware оформление элементов управления картой

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`; context: `testing-dotnet`, `session-insights-context`.
- Владелец: Codex / пользователь.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка.
- Ограничения: до подтверждения менять только эту спецификацию; после подтверждения не менять публичный API и не менять поведение навигации карты.
- Связанные ссылки: центральный `C:\Users\Kibnet\.codex\agents\AGENTS.md`, локальный `AGENTS.override.md`.

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
Локализовать элементы управления картой Roadmap и привести их overlay-оформление к теме приложения вместо жестко заданной темной палитры.

Outcome contract:
- Success means: все видимые/доступные подписи контролов карты берутся из localization resources; overlay-кнопки, панель управления, индикатор обновления и миникарта используют theme-aware ресурсы; существующие команды zoom/pan/fit/reset/collapse/expand работают как раньше.
- Итоговый артефакт / output: код UI + ресурсы локализации + headless UI regression test.
- Stop rules: остановиться, если найдено несколько несовместимых UX-вариантов без явно лучшего; если targeted UI tests не запускаются из-за окружения, зафиксировать blocker и next-best evidence.

## 2. Текущее состояние (AS-IS)
- Основной UI карты находится в `src/Unlimotion/Views/GraphControl.axaml`, логика overlay-команд находится в `src/Unlimotion/Views/GraphControl.axaml.cs`.
- Ресурсные строки живут в `src/Unlimotion.ViewModel/Resources/Strings.resx` и `src/Unlimotion.ViewModel/Resources/Strings.ru.resx`; `App.axaml.cs` публикует ключи в `Application.Resources`, поэтому XAML использует `DynamicResource`.
- В `GraphControl.axaml` фильтры карты уже локализованы через `DynamicResource`, но viewport/minimap controls используют английские tooltips: `Zoom in`, `Zoom out`, `Fit`, `Reset`, `Pan up/down/left/right`, `Hide graph controls`, `Show graph controls`, `Hide minimap`, `Show minimap`.
- В этих же контролах используются грубые текстовые символы `[]`, `T`, `M`, `x`, `^`, `<`, `>`, `v`; они выглядят как debug UI и плохо масштабируются визуально.
- Overlay-панели и кнопки карты жестко задают цвета `#3A3A3A`, `#707070`, `White`, `#CC1E1E1E`, `#E61E1E1E`, `#202020`, `#808080`, `#B0B0B0`, поэтому UI не следует светлой/темной теме.
- Существующие headless UI tests для overlay карты находятся в `src/Unlimotion.Test/RoadmapGraphUiTests.cs`: `RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls` и `RoadmapGraph_ViewportOverlays_CollapseToCompactButtonsAndRestore`.

## 3. Проблема
Элементы управления картой выглядят неаккуратно, частично англоязычны в русской локали и используют фиксированную темную палитру, из-за чего нарушают единый UI-контракт приложения.

## 4. Цели дизайна
- Разделение ответственности: строки в resx, визуальный стиль в XAML styles, поведение команд без изменений.
- Повторное использование: использовать существующий `DynamicResource` localization pipeline и существующие `Theme*Brush` ресурсы Avalonia/Fluent.
- Тестируемость: расширить существующие Avalonia.Headless/TUnit tests, не вводя новый test harness.
- Консистентность: сохранить `AutomationProperties.AutomationId`; добавить/проверить `AutomationProperties.Name` через локализованные строки.
- Обратная совместимость: не менять binding, команды, persisted state и selection/viewport алгоритмы.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем алгоритмы layout карты, zoom, pan, fit, reset, selection, minimap binding.
- Не меняем структуру `GraphViewModel` и не добавляем persisted settings.
- Не меняем global theme system и не создаем новый icon package.
- Не трогаем фильтры Roadmap, кроме случайной совместимости со стилями, если она потребуется.
- Не создаем крупные video artifacts в репозитории.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.ViewModel/Resources/Strings.resx` -> английские строки действий карты.
- `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` -> русские строки действий карты.
- `src/Unlimotion/Views/GraphControl.axaml` -> theme-aware brush resources, локализованные tooltips/names, аккуратные icon/content controls.
- `src/Unlimotion.Test/RoadmapGraphUiTests.cs` -> headless assertions для локализации, accessibility names и theme resource bindings.

### 6.2 Детальный дизайн
- Добавить ключи вида `RoadmapZoomIn`, `RoadmapZoomOut`, `RoadmapFitViewport`, `RoadmapResetViewport`, `RoadmapPanUp`, `RoadmapPanDown`, `RoadmapPanLeft`, `RoadmapPanRight`, `RoadmapHideGraphControls`, `RoadmapShowGraphControls`, `RoadmapHideMinimap`, `RoadmapShowMinimap`.
- Для каждой кнопки overlay поставить `ToolTip.Tip="{DynamicResource ...}"` и `AutomationProperties.Name="{DynamicResource ...}"`.
- Заменить `Content="[]"`, `Content="T"`, `Content="M"`, `Content="x"` на компактные `PathIcon` внутри существующих кнопок. Для `+`, `-` и стрелок допустимы текстовые символы только если они визуально стабильны и имеют локализованный accessible name; предпочтительно также перевести pan/fit/collapse/expand на `PathIcon`, чтобы UI выглядел единообразно.
- Стили `roadmapViewportButton` и `roadmapOverlayButton` должны использовать theme resources: `ThemeControlLowBrush`, `ThemeControlMidBrush`, `ThemeControlHighBrush`/`ThemeForegroundBrush` там, где ресурс доступен в текущем проекте. Не использовать hard-coded dark background/white foreground для основного overlay UI.
- Панели `RoadmapViewportToolbar`, `RoadmapMinimapPanel` и `RoadmapBuildIndicator` должны использовать `DynamicResource` theme brushes с легкой рамкой и прежними размерами, чтобы не ломать layout.
- `RoadmapMinimap` и его item template должны уйти от фиксированной темной палитры к theme-aware brushes; допустимо оставить только минимальные accent/selection colors, если они соответствуют существующим app resources.
- Visual planning artifact для UI-facing изменений:

```text
Roadmap surface
┌──────────────────────────────────────────────┐
│ [localized filter/search toolbar unchanged]  │
│                                              │
│                                              │
│ ┌ controls panel, themed ┐     ┌ minimap ┐   │
│ │ + - fit reset   ↑      │     │ themed  │   │
│ │              ←     →   │     │ nodes   │   │
│ │                 ↓  x   │     │      x  │   │
│ └───────────────────────┘     └─────────┘   │
│ collapsed state: [controls icon] [map icon]  │
└──────────────────────────────────────────────┘
```

- UI/video evidence:
  - Записать `before` baseline до изменения кода через skill `record-app-screen`, focused на окне desktop-приложения Unlimotion.
  - Записать `after` после реализации тем же способом, с тем же размером окна и тем же сценарием.
  - Сценарий записи: открыть Roadmap, показать overlay controls и minimap, выполнить zoom/pan/reset, свернуть/развернуть controls и minimap.
  - Видео не коммитить по умолчанию; вернуть absolute paths в итоговом отчете и указать duration/FPS/caveats.
  - Fallback допустим только если запись окна технически невозможна: `ffmpeg` недоступен, окно приложения не запускается/не находится, recorder script отсутствует или захват небезопасен. Тогда указать причину, команду проверки и next-best evidence.
- Границы сохранения поведения: `AutomationId`, command handlers, click behavior, compact collapsed button size <= 40 и viewport/minimap bindings сохраняются.
- Обработка ошибок: новые resource keys должны существовать в обеих resx; отсутствующий key считается тестовым failure.
- Производительность: XAML style/resource lookup не должен менять графовый build path; дополнительных подписок или таймеров не добавлять.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Для русской локали пользовательские подписи должны быть русскими и не содержать случайного English UI copy.
- Для английской локали fallback-строки должны оставаться английскими.
- Доступность: кнопки с icon-only content обязаны иметь `AutomationProperties.Name`.

## 8. Точки интеграции и триггеры
- `GraphControl.axaml` использует `DynamicResource` и автоматически получает новые значения после обновления application resources при смене языка.
- `RoadmapZoomIn_OnClick`, `RoadmapZoomOut_OnClick`, `RoadmapFit_OnClick`, `RoadmapResetViewport_OnClick`, `RoadmapPan*`, collapse/expand handlers остаются без изменения.
- Headless tests создают `App`, `MainControl` и Roadmap tab, затем проверяют реальные controls по stable automation ids.

## 9. Изменения модели данных / состояния
- Новых persisted или calculated state fields нет.
- В хранилище задач, настройках пользователя и view models изменений нет.

## 10. Миграция / Rollout / Rollback
- Первый запуск: новые строки подхватываются из resx без миграции.
- Обратная совместимость: отсутствие изменений в persisted data и публичных командах.
- Rollback: revert изменений в `GraphControl.axaml`, двух resx и тесте.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В `GraphControl.axaml` не остается английских hard-coded tooltip для Roadmap overlay controls.
  - У icon-only overlay buttons есть локализованные `ToolTip.Tip` и `AutomationProperties.Name`.
  - Overlay controls и minimap/progress panels используют `DynamicResource` theme brushes вместо фиксированной темной палитры для background/border/foreground.
  - Существующие zoom/pan/reset/collapse/expand сценарии проходят без изменения поведения.
  - Русские и английские resource keys добавлены симметрично.
- Какие тесты добавить/изменить:
  - Обновить `RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls`: проверить локализованные names/tooltips для основных buttons и сохранить проверки zoom/pan/reset.
  - Обновить `RoadmapGraph_ViewportOverlays_CollapseToCompactButtonsAndRestore`: проверить localized names/tooltips для collapse/expand minimap/controls, размеры collapsed buttons и восстановление панелей.
  - При необходимости добавить helper для чтения `AutomationProperties.Name` и `ToolTip.Tip`.
- Characterization tests / contract checks для текущего поведения: существующие assertions по `ViewportZoom`, `ViewportLocation`, collapse/restore и minimap binding остаются.
- Visual acceptance для UI-facing изменений: overlay должен соответствовать wireframe: icon-only buttons, компактные панели, без debug text `[]`/`T`/`M`/`x`, theme-aware colors.
- UI video evidence для UI-facing фич/багфиксов:
  - `before`: `artifacts/roadmap-map-controls-localization-theme/roadmap-controls-before.mp4`, записать до правок.
  - `after`: `artifacts/roadmap-map-controls-localization-theme/roadmap-controls-after.mp4`, записать после правок.
  - Команда: использовать `record-app-screen` / `scripts/record_app_window.ps1` с `-WindowTitle` или `-ProcessName` для окна Unlimotion, duration около 20 секунд, без audio.
  - Проверка: `Get-Item` для nonzero size; `ffprobe`, если доступен, для duration/dimensions.
  - Fallback: только при технической невозможности записи, с явной причиной и next-best evidence.
- Базовые замеры до/после для performance tradeoff: Не применимо, нет performance-sensitive path.
- Команды для проверки:

```powershell
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_ViewportOverlay_*" --no-progress
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_ViewportOverlays_*" --no-progress
dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --no-restore
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj
git diff --check
```

- Stop rules для test/retrieval/tool/validation loops: если targeted UI tests падают, фиксить до PASS; если full run занимает непропорционально долго или упирается в known environment failure, отчет должен явно отделить targeted evidence от неполного full evidence.

## 12. Риски и edge cases
- Риск: выбранный theme brush отсутствует в текущей Fluent/Nodify resource dictionary. Смягчение: использовать только уже встречающиеся в проекте `ThemeControlLowBrush`, `ThemeControlMidBrush`, `ThemeBackgroundBrush` или проверить через headless render/resource lookup.
- Риск: `DynamicResource` в `AutomationProperties.Name` не обновится так, как ожидается. Смягчение: тестировать через реально созданный `App` и текущий localization resource pipeline.
- Риск: icon-only content ухудшит понятность. Смягчение: stable tooltips/accessibility names и знакомые pictograms; не менять command placement.
- Риск: изменение hard-coded colors может слегка поменять контраст. Смягчение: использовать стандартные theme resources и проверить светлую/темную тему, если доступно в headless.

## 13. План выполнения
1. Добавить resource keys в обе resx.
2. Обновить `GraphControl.axaml`: локализованные tooltips/names, аккуратные icon content, theme-aware brushes.
3. Обновить `RoadmapGraphUiTests.cs`: assertions по localization/accessibility/theme style и сохранить behavioral assertions.
4. Запустить targeted UI tests.
5. Запустить build, затем full test command или явно зафиксировать объективный blocker.
6. Выполнить post-EXEC review-loop и исправить однозначные findings.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля:
  - SPEC фиксирует UI visual planning artifact.
  - План включает Avalonia.Headless UI tests.
  - План сохраняет stable `AutomationId`.
  - План включает `dotnet build`, targeted UI tests и full test attempt/evidence.
  - Video evidence планируется через `record-app-screen`; fallback допустим только при технической невозможности записи окна.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/GraphControl.axaml` | Локализованные names/tooltips, theme-aware brushes, аккуратные icon-only controls | Основной UI дефект |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | Английские ключи действий Roadmap overlay | Localization fallback |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | Русские ключи действий Roadmap overlay | Русская локализация |
| `src/Unlimotion.Test/RoadmapGraphUiTests.cs` | Headless UI regression assertions | Обязательное UI coverage |
| `specs/2026-06-16-roadmap-map-controls-localization-theme.md` | QUEST рабочая спецификация | Governance gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Tooltips | Hard-coded English | `DynamicResource` EN/RU |
| Accessibility names | Частично отсутствуют на icon/debug buttons | Локализованные `AutomationProperties.Name` |
| Button content | `[]`, `T`, `M`, `x`, raw arrows | Аккуратные icon/symbol controls с labels в tooltip/name |
| Colors | Fixed dark palette | Theme resources |
| Tests | Проверяли только поведение overlay | Проверяют поведение + localization/accessibility/theme contract |

## 18. Альтернативы и компромиссы
- Вариант: оставить текстовые символы, только локализовать tooltips.
- Плюсы: минимальный diff.
- Минусы: UI остается неаккуратным и часть проблемы пользователя не решена.
- Почему выбранное решение лучше в контексте этой задачи: замена debug-like content на icon-only controls с локализованными names закрывает и polish, и доступность, не меняя behavior.

- Вариант: добавить новый icon package.
- Плюсы: единая библиотека иконок.
- Минусы: лишняя dependency для маленькой UI правки.
- Почему выбранное решение лучше в контексте этой задачи: локальные `PathIcon` уже используются в проекте и достаточны для узкого Roadmap overlay.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, state, rollback и visual artifact описаны. |
| C. Безопасность изменений | 11-13 | PASS | Нет data migration; rollback простой; риски и edge cases перечислены. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, UI tests и команды проверки указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, соответствия и альтернативы достаточны; открытых вопросов нет. |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client` и `ui-automation-testing` требования отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна корневая UI-проблема, явные Non-Goals. |
| 2. Понимание текущего состояния | 5 | Указаны конкретные файлы, текущие hard-coded strings/colors и существующие тесты. |
| 3. Конкретность целевого дизайна | 5 | Есть key list, XAML contract, theme strategy и wireframe. |
| 4. Безопасность (миграция, откат) | 5 | Нет persisted data; rollback ограничен четырьмя рабочими файлами. |
| 5. Тестируемость | 5 | Acceptance criteria связаны с headless UI tests и командами. |
| 6. Готовность к автономной реализации | 5 | Открытых вопросов нет; план и stop rules заданы. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-roadmap-map-controls-localization-theme.md`, instruction stack (`model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `session-insights-context`, local override), selected profile `dotnet-desktop-client` + `ui-automation-testing`, open questions, planned changed files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: просмотрены central/local instructions, `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs`, `App.axaml`, test csproj, localization/resource search results.
  - Contract pass: spec не разрешает код до approval, содержит UI visual artifact, UI tests, validation commands, Non-Goals и `before`/`after` video evidence plan.
  - Adversarial risk pass: проверены риски отсутствующих theme resources, неработающего DynamicResource для automation name, ухудшения контраста и drift от поведения карты.
  - Re-review after fixes / Fix and re-review: не требовалось, блокирующих findings нет.
  - Stop decision: PASS, можно передать пользователю на утверждение.
- Evidence inspected: hard-coded Roadmap overlay strings/colors in `GraphControl.axaml`; existing Roadmap overlay tests; existing theme resource usage in nearby XAML; TUnit package in test project; central QUEST/UI automation docs.
- Depth checklist:
  - Scope drift / unrelated changes: planned files ограничены Roadmap overlay localization/theme/test/spec.
  - Acceptance criteria: проверяемые и связаны с тестами.
  - Validation evidence: до EXEC только inspected evidence; после EXEC обязателен targeted UI test/build/full attempt.
  - Unsupported claims: video evidence не подменяет UI tests; fallback ограничен техническими blockers записи окна.
  - Regression / edge case: covered by preserving command handlers and existing behavioral assertions.
  - Comments/docs/changelog: comments не планируются; changelog не нужен для small UI fix.
  - Hidden contract change: stable `AutomationId` сохранены; accessible names добавляются.
  - Manual-review challenge: вероятная ручная находка была бы "цвета все еще hard-coded" или "английский tooltip остался"; это вынесено в acceptance.
- No-findings justification: spec покрывает governance, UI plan, tests, rollback и не оставляет блокирующих открытых вопросов.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence зависит от запуска desktop app и доступности recorder/ffmpeg, а не от Avalonia.Headless runner | Во время EXEC записать `before`/`after` через `record-app-screen`; fallback использовать только при техническом blocker | accepted-risk |

- Fixed before continuing: Не применимо.
- Checks rerun: ручная self-check по spec-linter/spec-rubric/review-loop.
- Needs human: требуется фраза `Спеку подтверждаю`.
- Residual risks / follow-ups: возможна дополнительная визуальная проверка screenshot после EXEC, если headless assertions не дают достаточного confidence по contrast/polish.

### Post-EXEC Review
- Статус: PASS с validation caveat по unrelated full-suite failure
- Scope reviewed: `GraphControl.axaml`, `Strings.resx`, `Strings.ru.resx`, `RoadmapGraphUiTests.cs`, generated evidence artifacts, validation logs.
- Decision: изменение соответствует утвержденной SPEC; блокирующих findings по Roadmap localization/theme нет.
- Review passes:
  - Scope/Evidence pass: diff ограничен Roadmap overlay XAML, EN/RU resources, Roadmap UI tests и этой SPEC.
  - Contract pass: `AutomationId` сохранены; command handlers не менялись; persisted state/view model/data model не затронуты.
  - Adversarial risk pass: проверено отсутствие старых hard-coded tooltip/content/colors в `GraphControl.axaml`; theme brushes проверены headless-тестами через отсутствие прежних fixed colors.
  - Re-review after fixes / Fix and re-review: self-review усилил тест, добавив assertions для всех `RoadmapPan*Button`, затем affected targeted test повторно прошел.
  - Stop decision: PASS; можно завершать задачу без дополнительных правок.
- Evidence inspected: diff, `rg` по старым Roadmap overlay strings/colors, targeted TUnit output, desktop build output, full test output, `before`/`after` MP4 metadata.
- Depth checklist:
  - Scope drift / unrelated changes: нет unrelated edits в tracked files; video/log artifacts лежат в `artifacts/` и не коммитятся по умолчанию.
  - Acceptance criteria: закрыты локализация tooltips/names, icon-only content, theme-aware brushes, сохранение поведения zoom/pan/reset/collapse/expand.
  - Validation evidence: targeted Roadmap UI tests PASS; build PASS; `git diff --check` PASS с CRLF warnings; full run выполнен и имеет unrelated failure.
  - Unsupported claims: visual polish подтвержден `after` video; theme contract подтвержден XAML diff и headless assertions против прежних fixed colors.
  - Regression / edge case: command handlers и bindings сохранены; collapse/restore/minimap binding covered by existing behavioral assertions.
  - Comments/docs/changelog: новых code comments не требуется; changelog не нужен для scoped UI fix.
  - Hidden contract change: `AutomationProperties.AutomationId` сохранены, добавлены только localized `AutomationProperties.Name`.
  - Manual-review challenge: вероятный вопрос "все ли pan-кнопки локализованы" закрыт дополнительными assertions по up/left/right/down.
- No-findings justification: self-review не нашел blocking/medium issues; единственная validation caveat относится к существующему `MainControlTaskCardLayoutUiTests`, не к Roadmap overlay.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full suite failed in unrelated `MainControlTaskCardLayoutUiTests.CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls`; Roadmap targeted tests passed | Не блокировать этот scoped fix; при необходимости разбирать layout test отдельно | residual-risk |

- Fixed before final report: усилен `RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls` assertions для `RoadmapPanUpButton`, `RoadmapPanLeftButton`, `RoadmapPanRightButton`, `RoadmapPanDownButton`.
- Checks rerun:
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_ViewportOverlay_*" --no-progress` -> PASS, 1/1, 5s439ms после усиления теста.
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_ViewportOverlays_*" --no-progress` -> PASS, 1/1, 5s085ms.
  - `dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --no-restore -v:minimal` -> PASS, 0 errors, existing CRLF warnings.
  - `git diff --check` -> PASS, existing CRLF warnings only.
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --no-progress` -> FAIL unrelated: total 531, succeeded 530, failed 1, duration 5m37s426ms.
- Validation evidence:
  - `artifacts/roadmap-map-controls-localization-theme/roadmap-controls-before.mp4`: 204896 bytes, 2880x1498, 24.000s.
  - `artifacts/roadmap-map-controls-localization-theme/roadmap-controls-after.mp4`: 246073 bytes, 2880x1498, 23.900s.
- Unrelated changes: tracked unrelated changes не обнаружены; внешний `dotnet test ... -c Release --no-restore` процесс был найден, но не запускался в рамках этой задачи и не останавливался.
- Needs human: нет.
- Residual risks / follow-ups: full-suite layout failure стоит разобрать отдельной задачей, если нужен полностью зеленый локальный прогон.

## Approval
Получено: пользователь написал "Спеку подтверждаю".

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор instruction stack и памяти | 0.9 | Нет | Инспектировать текущий Roadmap UI и тесты | Нет | Нет | Central stack требует SPEC-first; память подсказала релевантный Roadmap test surface, затем проверено в workspace | `AGENTS.md`, `AGENTS.override.md`, memory index |
| SPEC | Инспекция AS-IS | 0.9 | Нет | Сформировать SPEC | Нет | Нет | Найдены hard-coded English tooltips/debug content/theme colors и существующие overlay UI tests | `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs`, `App.axaml`, `Unlimotion.Test.csproj` |
| SPEC | Создание и self-review спецификации | 0.9 | Нет | Запросить подтверждение пользователя | Да | Да, требуется фраза `Спеку подтверждаю` | SPEC содержит design, visual artifact, acceptance, test plan и post-SPEC review | `specs/2026-06-16-roadmap-map-controls-localization-theme.md` |
| SPEC | Уточнение video evidence | 0.95 | Нет | Запросить подтверждение пользователя | Да | Да, пользователь указал на доступные video skills; требуется фраза `Спеку подтверждаю` | Исправлен слишком слабый fallback: `before`/`after` video теперь обязательный EXEC-план через `record-app-screen`, fallback только при техническом blocker | `specs/2026-06-16-roadmap-map-controls-localization-theme.md` |
| EXEC | Получение approval | 1.0 | Нет | Записать `before` video до изменений кода | Нет | Да, пользователь написал `Спеку подтверждаю` | SPEC gate открыт; baseline video должен быть снят до первого изменения реализации | `specs/2026-06-16-roadmap-map-controls-localization-theme.md` |
| EXEC | Запись baseline video | 0.9 | Нет | Изменить XAML/resources/tests | Нет | Нет | До правок кода записано `before` MP4: 24s, 30 FPS, 2880x1498, isolated config, scripted Roadmap overlay hover/click flow | `artifacts/roadmap-map-controls-localization-theme/roadmap-controls-before.mp4` |
| EXEC | Реализация и targeted UI tests | 0.9 | Нет | Собрать desktop и записать `after` video | Нет | Нет | Добавлены EN/RU resource keys, icon-only Roadmap controls, локальные theme brushes и assertions по localization/accessibility/theme; два targeted Roadmap overlay теста прошли | `GraphControl.axaml`, `Strings.resx`, `Strings.ru.resx`, `RoadmapGraphUiTests.cs` |
| EXEC | Запись `after` video | 0.95 | Нет | Запустить build/full validation и self-review | Нет | Нет | После реализации записано `after` MP4 с тем же Roadmap overlay flow; `ffprobe` подтвердил 2880x1498 и 23.9s | `artifacts/roadmap-map-controls-localization-theme/roadmap-controls-after.mp4` |
| EXEC | Валидация | 0.9 | Full suite имеет unrelated failure | Выполнить post-EXEC review и финальный отчет | Нет | Нет | Scoped Roadmap tests прошли; desktop build и diff check прошли; full run дал 530/531 PASS и один unrelated layout failure | `full-test.out.log`, `RoadmapGraphUiTests.cs`, `Unlimotion.Desktop.csproj` |
| EXEC | Post-EXEC review | 0.95 | Нет | Завершить задачу | Нет | Нет | Self-review не нашел Roadmap blockers; тест усилен для всех pan-кнопок и повторно прошел | `specs/2026-06-16-roadmap-map-controls-localization-theme.md`, `RoadmapGraphUiTests.cs` |
