# Усилить границу карточки задачи

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`.
- Владелец: пользователь / Codex.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущий detached worktree `HEAD`; отдельная ветка не создана.
- Ограничения: QUEST mode; до фразы `Спеку подтверждаю` менять только эту спецификацию; UI-facing изменение обязано иметь UI test coverage и запуск релевантных UI tests; не менять ViewModel/domain/storage.
- Связанные ссылки: `AGENTS.override.md`, `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `instructions/core/quest-mode.md`, `instructions/profiles/dotnet-desktop-client.md`, `instructions/profiles/ui-automation-testing.md`, `specs/2026-05-14-task-card-redesign.md`, `specs/2026-05-15-task-card-visual-polish.md`, `specs/2026-06-04-task-card-dense-redesign.md`.

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Сделать границу текущей карточки задачи в правой панели `MainControl` более различимой, потому что сейчас визуальная граница теряется на фоне.

Outcome contract:
- Success means: панель деталей текущей задачи сохраняет текущий layout и адаптивность, но внешний бордер всей панели заметно отделяет ее от окружающей области в light и dark theme.
- Итоговый артефакт / output: scoped XAML style change для `Border.CurrentTaskDetailsPanelFrame` и regression UI test в `MainControlTaskCardLayoutUiTests`.
- Stop rules: остановиться после scoped реализации, passing targeted UI test/class run, `dotnet build` или объективного отчета о невозможности проверки, затем post-EXEC review.

## 2. Текущее состояние (AS-IS)
- Карточка текущей задачи находится в `src/Unlimotion/Views/MainControl.axaml`.
- Style `Border.CurrentTaskCard` сейчас задает `Padding="10"`, `Background="{DynamicResource ThemeControlLowBrush}"`, `BorderBrush="{DynamicResource ThemeControlMidBrush}"`, `BorderThickness="1"`, `CornerRadius="6"`.
- Markup карточки имеет `AutomationProperties.AutomationId="CurrentTaskCard"` и `Classes="CurrentTaskCard"`.
- Релевантная UI coverage уже есть в `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`; класс проверяет desktop/phone layout, compact mode, dark theme и стабильные automation ids.
- Предыдущие task-card спеки фиксируют, что у карточки должен быть один top-level border с radius `<= 8`, секции не должны превращаться во вложенные cards, а selectors/automation ids должны оставаться стабильными.
- EXEC discovery: при добавлении regression assertion текущие `ThemeControl*Brush` в headless Avalonia 12 контексте резолвились в `null`, поэтому итоговое решение использует scoped brushes поверх Fluent `SystemBaseMediumColor` и `SystemChromeLowColor`.
- EXEC correction after screenshot review: border на `CurrentTaskCard` визуально обрывался ниже контента; целевой элемент перенесен на `CurrentTaskDetailsPanelFrame`, который оборачивает весь `CurrentTaskDetailsScrollViewer`.

## 3. Проблема
Одна корневая проблема: top-level бордер карточки использует слишком мягкий theme brush относительно фона, поэтому пользователь не всегда видит границу рабочей области задачи.

## 4. Цели дизайна
- Разделение ответственности: изменить только визуальный style карточки, без логики и модели.
- Повторное использование: использовать существующие theme resources Avalonia/Fluent, без локального hardcoded palette, если существующий ресурс дает нужный контраст.
- Тестируемость: закрепить конкретный border contract через headless UI test на `CurrentTaskDetailsPanelFrame`.
- Консистентность: сохранить плотный рабочий UI, `CornerRadius=6`, текущие отступы и секционные dividers.
- Обратная совместимость: не менять automation ids, bindings, команды, layout containers и persisted state.

## 5. Non-Goals (чего НЕ делаем)
- Не редизайним карточку задачи целиком.
- Не меняем расположение controls, command bar, relation picker, section dividers и compact mode.
- Не добавляем новые глобальные ресурсы темы и hardcoded palette; допускаются только локальные scoped brushes, если старые alias-ресурсы темы не резолвятся надежно в текущем Avalonia 12 контексте.
- Не меняем `BorderThickness` больше чем нужно для читаемости; предпочтение - усилить brush, чтобы не менять layout.
- Не добавляем screenshot/video artifacts в git.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> добавить scoped brushes для панели деталей, обернуть `CurrentTaskDetailsScrollViewer` в `Border.CurrentTaskDetailsPanelFrame` и применить к нему видимые `BorderBrush`/`Background`.
- `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` -> добавить/расширить UI assertion, что вся панель деталей использует ожидаемый видимый border brush и сохраняет толщину `1`, а `CurrentTaskCard` остается контентным контейнером без короткой отдельной рамки.

### 6.2 Детальный дизайн
- Потоки данных: не меняются; это static style binding на Avalonia dynamic resources.
- Контракты / API: публичные API, bindings и automation ids не меняются.
- Output contract / evidence rules: evidence = failing-before/passing-after UI assertion на `CurrentTaskDetailsPanelFrame` плюс targeted class run и screenshot review.
- Visual planning artifact для UI-facing изменений:

```text
До:
details pane background
  [ details pane/card content: low background + mid border ]  граница слабо отделяется и может обрываться ниже контента

После:
details pane background
  +---------------------------------------------+
  | CurrentTaskDetailsPanelFrame: chrome-low bg + base-medium border |
  | существующий header/sections/relations       |
  +---------------------------------------------+
```

- UI test video evidence для UI automation задач: `Не применимо` как обязательный artifact на SPEC-фазе. Для EXEC fallback: текущий `Avalonia.Headless` suite не фиксирует видео в принятом локальном workflow; next-best evidence - deterministic headless UI assertion, targeted test output, и при необходимости screenshot через `tests/Unlimotion.ReadmeMedia`.
- Границы сохранения поведения: `CurrentTaskCard` остается контентным контейнером с padding; внешний border/background принадлежит `Border.CurrentTaskDetailsPanelFrame`; layout, bindings and compact behavior remain unchanged unless test proves a direct issue.
- Обработка ошибок: не применимо; новых error paths нет.
- Производительность: не меняется; замена brush не добавляет subscriptions/traversals.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Не применимо: нет доменного алгоритма.
- UI invariant: top-level card border must be visibly distinct from the card background in light and dark theme.

## 8. Точки интеграции и триггеры
- Avalonia style selector `Border.CurrentTaskDetailsPanelFrame` применяется при materialization `MainControl`.
- Dynamic resource должен корректно переоцениваться при theme variant changes.
- UI test должен создавать arranged `MainControl`, находить `CurrentTaskDetailsPanelFrame` по automation id и проверять border contract.

## 9. Изменения модели данных / состояния
- Новых полей нет.
- Persisted state не меняется.
- ViewModel/domain/storage не меняются.

## 10. Миграция / Rollout / Rollback
- Первый запуск: без миграции.
- Обратная совместимость: XAML selectors и automation ids сохраняются.
- Rollback: вернуть прежние `BorderBrush="{DynamicResource ThemeControlMidBrush}"` и `Background="{DynamicResource ThemeControlLowBrush}"`, удалить scoped brushes карточки и удалить/скорректировать новый test assertion.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `CurrentTaskDetailsPanelFrame` имеет `BorderThickness == new Thickness(1)`.
  - `CurrentTaskDetailsPanelFrame.BorderBrush` использует scoped `CurrentTaskDetailsPanelBorderBrush`, резолвящийся в Fluent `SystemBaseMediumColor`.
  - `CurrentTaskDetailsPanelFrame.Background` использует scoped `CurrentTaskDetailsPanelBackgroundBrush`, резолвящийся в Fluent `SystemChromeLowColor`.
  - `CurrentTaskCard` не рисует отдельную короткую panel border/background и остается content container.
  - Существующие desktop/phone layout assertions в `MainControlTaskCardLayoutUiTests` продолжают проходить.
  - Нет изменений в ViewModel/domain/storage.
- Какие тесты добавить/изменить:
  - Добавить assertion в `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls` или отдельный тест `CurrentTaskCard_UsesVisibleThemeAwareBorder`.
  - Для dark theme расширить существующий `CurrentTaskCard_DarkTheme_UsesThemeAwareAccentButtonChrome` проверкой border/background distinction либо добавить отдельный dark-theme case.
- Characterization tests / contract checks для текущего поведения: новый assertion сначала показал, что старые `ThemeControl*Brush` в текущем тестовом контексте резолвятся в `null`; screenshot review затем показал неверный target element, поэтому итоговый assertion проходит на `CurrentTaskDetailsPanelFrame` и дополнительно проверяет, что `CurrentTaskCard` не рисует отдельную рамку.
- Visual acceptance для UI-facing изменений: low-fi artifact выше; проверить, что visual contract не требует дополнительных layout changes.
- UI video evidence для UI-facing фич/багфиксов: fallback допустим из-за отсутствия принятого video recorder в `src/Unlimotion.Test`; evidence = UI test output, optional generated screenshot path if created.
- Базовые замеры до/после для performance tradeoff: не применимо.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --output Detailed`
  - `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj --no-restore /nodeReuse:false`
  - Full suite target if time/environment allows: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed`
- Stop rules для test/retrieval/tool/validation loops:
  - Stop retrieval after confirming current card style, available theme resource, and test location.
  - Stop validation after targeted UI class + desktop build; full suite remains optional broader evidence when time/risk profile justifies it.

## 12. Риски и edge cases
- Риск: `SystemBaseMediumColor` окажется слишком сильным в dark theme. Смягчение: проверить dark-theme assertion; при необходимости заменить scoped brush на более мягкий Fluent resource без изменения layout.
- Риск: assertion станет слишком implementation-specific. Смягчение: проверять resource-equivalent brush/background distinction and thickness, not pixel screenshots.
- Риск: full suite может быть тяжелым/нестабильным из-за known shared-state issues. Смягчение: targeted class is required; full suite is attempted or reported with concrete blocker.

## 13. План выполнения
1. После `Спеку подтверждаю` добавить failing UI assertion на border contract.
2. Изменить scoped `Border.CurrentTaskDetailsPanelFrame` style в `MainControl.axaml`.
3. Запустить targeted `MainControlTaskCardLayoutUiTests`.
4. Запустить desktop build.
5. При возможности запустить full serial test command; если окружение блокирует, зафиксировать blocker и next-best evidence.
6. Выполнить post-EXEC review и обновить журнал спеки.

## 14. Открытые вопросы
Нет блокирующих вопросов. Product/UX decision принят автономно: усилить существующий top-level border через theme-aware resource, не меняя layout.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`; overlay `ui-automation-testing`.
- Выполненные требования профиля:
  - UI-поток и ViewModel API не меняются.
  - Stable automation ids сохраняются.
  - UI test coverage планируется в существующем `Avalonia.Headless` suite.
  - Релевантные UI tests обязательны перед завершением EXEC.
  - Video evidence имеет fallback с причиной и next-best evidence.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-16-task-card-border-contrast.md` | Новая рабочая SPEC | QUEST gate перед реализацией |
| `src/Unlimotion/Views/MainControl.axaml` | Добавить scoped brushes и `Border.CurrentTaskDetailsPanelFrame` вокруг `CurrentTaskDetailsScrollViewer` | Сделать границу всей панели деталей видимой |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Добавить/расширить assertion на border contract | Regression coverage для UI-facing fix |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Details panel border | Короткая рамка могла быть привязана к `CurrentTaskCard` и обрываться ниже контента | `CurrentTaskDetailsPanelFrame` на `SystemBaseMediumColor`, рамка идет по всей панели |
| Layout | Текущий dense/adaptive layout | Без изменений |
| Tests | Проверяют структуру и layout, не фиксируют border contrast | Проверяют structure/layout плюс border contract |

## 18. Альтернативы и компромиссы
- Вариант: увеличить `BorderThickness` до `2`.
- Плюсы: граница заметнее без смены цвета.
- Минусы: меняет внутреннюю геометрию и может выглядеть тяжелее в плотной панели.
- Почему выбранное решение лучше в контексте этой задачи: смена brush на более контрастный сохраняет layout contract и адресует именно нечитаемость границы.

- Вариант: добавить `BoxShadow`.
- Плюсы: визуально отделяет карточку.
- Минусы: пользователь просит бордер; тень усложнит визуальный стиль и может конфликтовать с рабочим dense UI.
- Почему выбранное решение лучше: явный border проще, дешевле и проверяемее.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals конкретны |
| B. Качество дизайна | 6-10 | PASS | Scoped XAML/test design, no data/runtime changes |
| C. Безопасность изменений | 11-13 | PASS | Rollback, risks и bounded execution plan указаны |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, tests и commands зафиксированы |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет блокирующих вопросов; alternatives and review included |
| F. Соответствие профилю | 20 | PASS | UI test/profile requirements reflected |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope is one visual border fix for existing task card |
| 2. Понимание текущего состояния | 5 | Current style, markup, tests and prior card specs inspected |
| 3. Конкретность целевого дизайна | 5 | Target brush/thickness behavior and fallback are concrete |
| 4. Безопасность (миграция, откат) | 5 | No data changes; rollback is one style revert plus test adjustment |
| 5. Тестируемость | 5 | Existing headless UI class and commands defined |
| 6. Готовность к автономной реализации | 5 | No open blockers; implementation plan is small and bounded |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-task-card-border-contrast.md`, instruction stack (`model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `quest-mode`, local override), selected profile, open questions, planned changed files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: inspected `MainControl.axaml` style/markup, `MainControlTaskCardLayoutUiTests.cs`, prior task-card specs, theme resource references.
  - Contract pass: spec preserves QUEST SPEC-only constraint, stable automation ids, UI test requirement and no model/storage change.
  - Adversarial risk pass: checked over-specific visual test risk, dark-theme contrast risk and full-suite stability risk.
  - Re-review after fixes / Fix and re-review: no fixes required after this review pass.
  - Stop decision: PASS; no BLOCKER/HIGH findings.
- Evidence inspected: original `Border.CurrentTaskCard` uses `ThemeControlMidBrush`; `CurrentTaskCard` automation id exists; existing theme aliases were considered before EXEC validation; `MainControlTaskCardLayoutUiTests` is the relevant headless suite.
- Depth checklist:
  - Scope drift / unrelated changes: only planned code files are XAML style and UI test.
  - Acceptance criteria: includes brush/background/thickness/layout preservation.
  - Validation evidence: targeted UI class, desktop build and optional full serial suite command listed.
  - Unsupported claims: claims are based on inspected style/test/resource references.
  - Regression / edge case: dark theme and implementation-specific assertion risks captured.
  - Comments/docs/changelog: no comments/docs/changelog needed beyond current SPEC.
  - Hidden contract change: none; public API, bindings, automation ids and storage preserved.
  - Manual-review challenge: likely reviewer question is whether the stronger border resource is too visible or not reliable; addressed by dark-theme check and fallback.
- No-findings justification: small scoped visual fix, existing test suite and concrete rollback make the design ready.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | No video artifact planned because local headless workflow has no accepted recorder | Use UI test output and optional screenshot fallback in EXEC | accepted-risk |

- Fixed before continuing: none.
- Checks rerun: SPEC linter/rubric reviewed manually.
- Needs human: confirmation phrase `Спеку подтверждаю`.
- Residual risks / follow-ups: full suite may be slow/flaky; if blocked, report exact blocker and targeted evidence.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, текущая SPEC, screenshot feedback от пользователя.
- Decision: можно завершать задачу после финального diff/status review.
- Review passes:
  - Scope/Evidence pass: изменения ограничены XAML style/resources, UI test assertions и SPEC; ViewModel/domain/storage не затронуты.
  - Contract pass: visible border contract перенесен на `CurrentTaskDetailsPanelFrame`; `CurrentTaskCard` остается content container с `AutomationId` и padding, но без отдельной короткой panel border/background.
  - Adversarial risk pass: проверены light/dark/resource distinction, responsive class scenarios и desktop build.
  - Re-review after fixes / Fix and re-review: stale SPEC assumptions about `ThemeControlHighBrush` replaced with actual `SystemBaseMediumColor`/`SystemChromeLowColor` contract; after screenshot feedback, target element corrected from `CurrentTaskCard` to `CurrentTaskDetailsPanelFrame`.
  - Stop decision: PASS; no BLOCKER/HIGH findings.
- Evidence inspected: failing-before characterization on old `ThemeControl*Brush` (`Current task card border should use ThemeControlHighBrush, got null.`), first screenshot set showing border tied to the wrong/short element, passing corrected targeted single test, passing targeted UI class, passing desktop build, corrected screenshots showing full-height panel border.
- Depth checklist:
  - Scope drift / unrelated changes: none observed in `git status`; only planned files modified.
  - Acceptance criteria: panel-frame border thickness, border/background resources, layout continuity and no model/storage changes covered.
  - Validation evidence: targeted `MainControlTaskCardLayoutUiTests` run passed 15/15; desktop build passed.
  - Unsupported claims: visual contrast claim is grounded in concrete resolved brush colors/resources and screenshot inspection of desktop/phone artifacts.
  - Regression / edge case: dark theme and phone widths covered by existing class run.
  - Comments/docs/changelog: no code comments/changelog required for this scoped visual fix; SPEC updated.
  - Hidden contract change: none; public APIs, bindings, commands, automation ids and persisted state unchanged.
  - Manual-review challenge: why not border on `CurrentTaskCard`; answer is screenshot evidence showed that element does not cover the lower panel area, while `CurrentTaskDetailsPanelFrame` covers the visual panel.
- No-findings justification: scoped style/test change passed the relevant UI class and build, corrected screenshots show full panel border, and no broader behavioral surface was touched.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Full serial suite was not run because this change is limited to a task-card style contract and the relevant UI class plus desktop build passed | Report targeted evidence and avoid extending scope into known heavy/flaky shared-state suite | accepted-risk |

- Fixed before final report: SPEC stale `ThemeControlHighBrush` assumptions corrected; screenshot feedback fixed by moving border/background from `CurrentTaskCard` to `CurrentTaskDetailsPanelFrame`.
- Checks rerun:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls" --output Detailed` -> passed 1/1.
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --output Detailed` -> passed 15/15.
  - `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj --no-restore /nodeReuse:false` -> passed, 2 warnings about LF/CRLF in modified files.
  - `dotnet run --project tests/Unlimotion.ReadmeMedia/Unlimotion.ReadmeMedia.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --ux-review task-card --language ru --output-root chat-artifacts/task-details-panel-border-20260616-ru --no-build-before-launch` -> generated 11 screenshots and `report.json`, no report warnings.
- Validation evidence: TUnit HTML report generated at `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/Unlimotion.Test-windows-net10.0-report.html`.
- Screenshot evidence inspected:
  - `chat-artifacts/task-details-panel-border-20260616-ru/desktop/root-description.png` -> visible full-height details panel border on desktop root-description scenario, including the area below task-card content.
  - `chat-artifacts/task-details-panel-border-20260616-ru/desktop/repeater-planning.png` -> visible details panel border with repeater controls.
  - `chat-artifacts/task-details-panel-border-20260616-ru/phone/root-description-card.png` -> visible details panel border on phone-width task-card view.
- Unrelated changes: none detected before final diff review.
- Needs human: no.
- Residual risks / follow-ups: none for the reported issue; generated screenshots remain local artifacts and are not committed.

## Approval
Получена фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершенный значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Контекст и discovery | 0.86 | Подтверждение пользователя на EXEC | Запросить `Спеку подтверждаю` | Да | Да, нужно получить confirmation phrase | Найдены текущий style, card automation id, theme resources и релевантный UI test suite; задача scoped к border contrast | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `specs/2026-06-16-task-card-border-contrast.md` |
| EXEC | Реализация и characterization | 0.89 | Нет | Запустить targeted UI class и build | Нет | Да, пользователь подтвердил EXEC фразой `Спеку подтверждаю` | Добавлен regression assertion; первая проверка показала `null` на старых `ThemeControl*Brush`, после чего XAML переведен на scoped brushes поверх Fluent `SystemBaseMediumColor`/`SystemChromeLowColor` | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | Validation и review | 0.93 | Нет | Финальный diff/status review и отчет пользователю | Нет | Нет | Single targeted test passed 1/1, full relevant `MainControlTaskCardLayoutUiTests` class passed 15/15, desktop build passed; screenshot capture generated 11 PNG artifacts; screenshot feedback found wrong target element | `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/Unlimotion.Test-windows-net10.0-report.html`, `chat-artifacts/task-card-border-20260616-ru`, `specs/2026-06-16-task-card-border-contrast.md` |
| EXEC | Screenshot feedback fix | 0.95 | Нет | Commit, push, update PR body | Нет | Да, пользователь указал, что ниже карточки панель не имеет бордера | Border/background перенесены на `CurrentTaskDetailsPanelFrame`; `CurrentTaskCard` оставлен content container; повторно прошли targeted UI tests/build и новые screenshots показывают border до низа панели | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `chat-artifacts/task-details-panel-border-20260616-ru`, `specs/2026-06-16-task-card-border-contrast.md` |
