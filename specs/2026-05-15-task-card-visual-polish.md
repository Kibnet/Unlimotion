# Task Card Visual Polish and Interaction Hierarchy

## 0. Метаданные
- Тип (профиль): `delivery-task`; профили `dotnet-desktop-client`, `ui-automation-testing`; контексты `testing-dotnet`, `visual-feedback`.
- Владелец: Codex / пользователь.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: follow-up после `specs/2026-05-15-task-card-mobile-ux-follow-up.md`; текущая PR-ветка `codex/task-card-redesign`, если владелец не выберет отдельную ветку.
- Ограничения: QUEST mode; до подтверждения менять только эту спецификацию; UI-facing изменение обязано иметь UI tests, visual evidence и релевантные проверки; Android/narrow width remains first-class target, но real Android evidence может быть заблокирован локальным SDK workload.
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `C:\Users\Kibnet\.codex\agents\templates\specs\_template.md`, `C:\Users\Kibnet\.codex\agents\instructions\core\quest-mode.md`, `C:\Users\Kibnet\.codex\agents\instructions\contexts\visual-feedback.md`, `C:\Users\Kibnet\.codex\agents\instructions\profiles\dotnet-desktop-client.md`, `C:\Users\Kibnet\.codex\agents\instructions\profiles\ui-automation-testing.md`, `specs/2026-05-14-task-card-redesign.md`, `specs/2026-05-15-task-card-mobile-ux-follow-up.md`, UX evidence `artifacts/ux-review/20260515-1732-task-card/`.

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Довести карточку задачи от "адаптивная и не ломается" до "удобная, визуально собранная и приятная в ежедневной работе". Эта спека фиксирует следующий UX/UI polish: понятная иерархия действий, более спокойный header, менее формальный planning, компактные relation groups и облегчённое описание.

Outcome contract:
- Success means: пользователь быстрее считывает название, состояние и основные действия задачи; на desktop карточка выглядит как рабочая side panel, а не большая техническая форма; на `390x844` phone width первый экран остаётся компактным и visually calm; relation editor stays usable.
- Итоговый артефакт / output: XAML/style/layout правки карточки, обновлённые UI selectors/tests where needed, regenerated UX screenshots via existing `--ux-review task-card`, updated spec journal and post-EXEC evidence.
- Stop rules: остановиться после выполнения acceptance criteria, targeted UI tests, desktop/test builds, screenshot capture evidence, post-EXEC review, или после явного отчёта о невозможности Android/emulator validation.

## 2. Текущее состояние (AS-IS)
- Текущая карточка реализована в `src/Unlimotion/Views/MainControl.axaml` и `src/Unlimotion/Views/MainControl.axaml.cs`.
- Есть responsive compact mode `TaskDetailsCompact`, layout-тесты в `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, selectors в `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`.
- UX capture command уже добавлен в `tests/Unlimotion.ReadmeMedia/Program.cs` и формирует evidence under `artifacts/ux-review/<stamp>-task-card/`.
- Последний evidence set: `artifacts/ux-review/20260515-1732-task-card/`, `Warnings: []`, desktop `1760x1060`, phone `390x844`.
- Что уже хорошо:
  - на `360/390/430` нет горизонтального overflow;
  - phone first viewport показывает compact commands and card header;
  - relation editor открывается и usable на phone width;
  - relation tree не вызывает горизонтальный скролл.
- Что осталось слабым:
  - все действия выглядят почти одинаково: primary, secondary, archive и destructive визуально конкурируют;
  - header перегружен сырыми metadata строками `id`, `Создано`, `Изменено`, `Разблокировано`;
  - planning на phone выглядит как длинная форма из повторяющихся пар "поле + большая кнопка";
  - relations функциональны, но визуально шумные: повторяются четыре заголовка + кнопки `Добавить`, красные удаления постоянно забирают внимание;
  - description содержит дублирующий placeholder `Описание` внутри секции `Описание`;
  - visual acceptance сейчас проверяет "не сломалось", но не фиксирует желаемую иерархию.

## 3. Проблема
Карточка стала технически адаптивной, но её визуальная иерархия всё ещё недостаточно продуктовая: пользователь видит много равнозначных controls и технических данных вместо спокойного фокуса на задаче, состоянии и следующем действии.

## 4. Цели дизайна
- Установить clear action hierarchy: primary, secondary, overflow and destructive.
- Сделать header task-first: title and completion dominate, metadata becomes secondary.
- Снизить визуальную тяжесть planning and description without removing existing capabilities.
- Сохранить relation editor model, но сделать relation groups visually quieter and easier to scan.
- Не ломать bindings, commands, persisted task model, existing `AutomationId` where possible.
- Сохранить phone usability at `360`, `390`, `430` widths and desktop density.
- Закрепить visual quality acceptance в screenshot checklist, чтобы future review ловил не только overflow.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем доменную модель задачи, storage schema, sync/backup logic, sorting/filtering and task tree behavior.
- Не добавляем новую глобальную design system.
- Не переписываем relation editor на другой сценарий поиска.
- Не скрываем critical task actions permanently; hidden actions must remain discoverable through overflow/flyout or context menu.
- Не добавляем relation counts/counters in this spec. Если нужны счётчики связей, это отдельное product/VM решение до EXEC.
- Не делаем Android-only UI fork.
- Не меняем semantics existing commands: create, archive, remove, planning quick set and relation add/remove must keep behavior.
- Не коммитим generated screenshot artifacts by default.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> visual hierarchy, button styles/classes, header/meta layout, description/planning/relation polish.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> only scoped width/layout helper updates if XAML styles cannot express responsive behavior; no task state changes.
- `src/Unlimotion.ViewModel/Resources/Strings.resx` and `Strings.ru.resx` -> only if new visible labels/tooltips are needed.
- `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` -> add/adjust assertions for hierarchy-critical controls and phone compact layout.
- `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` -> update selectors only for new stable controls.
- `tests/Unlimotion.ReadmeMedia/Program.cs` -> reuse existing `--ux-review task-card`; extend only if new screenshot state is required.
- `specs/2026-05-15-task-card-visual-polish.md` -> decisions, acceptance criteria, evidence and execution journal.

### 6.2 Детальный дизайн
#### Visual planning artifact
Target phone first viewport:

```text
+--------------------------------------------------+
| breadcrumb / task path                            |
| [Primary: Новая] [Еще v]                          |
|                                                  |
| [ ]  Task title editor                            |
|      [Важное] [95] [Archive]                      |
|      id launch-pilot  |  changed 15.05 11:10      |
|                                                  |
| Описание                                          |
| text area without duplicate inner label           |
|                                                  |
| Планирование                                      |
| Начало        16.05.2026       [Задать]           |
| Длительность  6h               [Задать]           |
| Окончание     17.05.2026       [Задать]           |
+--------------------------------------------------+
```

Target relations section:

```text
Связи
Родительские задачи                         [+]
  one quiet row, delete action less dominant
Блокирующие задачи                          [+]
  parent task
  child task
Содержащие задачи                           [+]
  empty state or quiet absence
Заблокированные задачи                      [+]
  inline relation editor when open
```

#### Button hierarchy
- Primary action:
  - `Новая` remains visible on desktop and phone.
  - It should have the strongest visual treatment in the command row, but not oversized.
  - On phone it remains one of two visible actions: `Новая` + `Еще`.
- Secondary actions:
  - `Соседняя`, `Соседняя blocked`, `Внутренняя`, `Переместить в папку` stay visible on desktop if space allows.
  - On phone they remain in `Еще`.
  - Secondary actions use quieter outline/neutral visual treatment.
- Destructive action:
  - `Удалить` should not look like a normal grey tile.
  - Prefer: text-danger / icon-danger in overflow or separated command group; avoid large red button unless confirming destructive operation.
- Archive:
  - `Архивировать` inside card header should read as a secondary state action, not as another primary command.
  - On phone it may be a compact chip/button in the meta row.

#### Header and metadata
- Title and completion are the primary header row.
- Wanted, importance and archive become compact state chips/actions:
  - wanted checkbox keeps current binding and selector;
  - importance keeps numeric editing but may be visually compact;
  - archive stays accessible and clearly secondary.
- Raw id and dates become muted metadata:
  - reduce font size/opacity;
  - use compact labels such as `id launch-pilot`, `создано 15.05 02:00`, `изменено 15.05 11:10`;
  - on phone allow wrapping to two short rows but avoid long technical sentences.
- Dates remain visible in the header metadata area when present. This spec does not move dates into a new hidden flyout/details surface.

#### Description
- Remove duplicated visual label inside text area when section title already says `Описание`.
- Keep placeholder only when the section title is not enough or when field is empty and needs an action prompt.
- Reduce empty description visual weight:
  - shorter min height for empty/short description;
  - keep enough room for editing without feeling cramped.

#### Planning
- Desktop:
  - keep three logical groups: begin, duration, end.
  - Reduce button weight; quick-set controls should look secondary to actual values.
- Phone:
  - keep the existing `CalendarDatePicker`, duration `TextBox` and quick-set `DropDownButton` controls with current bindings and automation ids;
  - make the visual treatment row-like only through spacing, width, typography and secondary button styling;
  - quick-set buttons become secondary compact actions next to or below the value depending on width;
  - do not replace current controls with a custom tappable row in this spec.
- Empty values must be quiet placeholders, not heavy disabled-looking boxes.

#### Repeater
- Keep existing repeater controls and weekday toggles.
- Improve spacing and hierarchy only enough to match planning polish.
- Do not redesign recurrence rules in this spec.

#### Relations
- Preserve four relation groups and existing inline add editor.
- Make group headers more scannable:
  - label left, add action right;
  - add action may be icon+tooltip on desktop/phone if accessibility remains clear.
- Reduce permanent red delete noise:
  - desktop: show delete affordance on hover/focus or make it visually quieter by default;
  - phone: keep touch-accessible trailing remove action, but less visually dominant than current red cross.
- Empty groups should not consume large space; show either header + add or a quiet empty hint if needed.
- Relation editor remains full-width inside current group and must show input, suggestions/empty state, cancel and confirm.

#### Visual style constraints
- Cards radius stays at or below 8px.
- No decorative orbs, gradients, marketing-style hero composition or nested cards.
- Use the existing theme resources where possible; avoid hard-coded one-off colors unless there is no available resource.
- Avoid one-note palette changes; this is a hierarchy polish, not a brand redesign.
- Text must not overlap, clip horizontally, or require horizontal scrolling at target widths.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Не применимо: task business logic does not change.
- UI invariants:
  - all existing commands keep current effects;
  - task completion, wanted, importance, archive and planning bindings remain two-way as today;
  - relation add/remove behavior remains identical;
  - screenshot capture uses isolated automation scenario and must not mutate persisted user data.

## 8. Точки интеграции и триггеры
- `MainControl` applies regular/compact styles based on existing `TaskDetailsCompact` behavior.
- Existing `DropDownButton` flyouts continue to trigger current command bindings.
- Existing AppAutomation/FlaUI demo scenario continues to select `launch-pilot`, `capture-readme-tour`, `publish-landing`.
- Existing UX capture command remains the source for visual review evidence.

## 9. Изменения модели данных / состояния
- Persisted task model: no changes.
- ViewModel API: no required changes.
- New calculated UI state: avoid if possible; allowed only for visual classes or command grouping.
- New localization keys: allowed for compact labels/tooltips if needed.
- Generated screenshots/reports: local artifacts under `artifacts/`, not committed by default.

## 10. Миграция / Rollout / Rollback
- Миграция: не требуется.
- Rollout: ship as UI-only polish on top of current task-card redesign.
- Rollback:
  - revert visual styles/layout blocks in `MainControl.axaml`;
  - revert any optional code-behind width helper changes;
  - keep screenshot capture command unless it is directly broken by this change.
- Compatibility: existing keyboard, automation and relation flows must continue to work.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Desktop screenshots show exactly one primary-accent command in the command row: `Новая`.
  - Secondary command buttons use a quieter neutral/outline treatment than `Новая`; `Удалить` is visually separated or danger-text styled and does not appear as a normal grey tile.
  - Phone `390x844` first viewport remains compact and shows command row, card title, state row and at least the beginning of description or planning without horizontal clipping.
  - Header metadata uses smaller/muted styling than the title and state controls, and all present date/id metadata remains visible or wrapped without clipping.
  - Description no longer displays duplicate `Описание` label inside an already titled section.
  - Planning keeps existing date/duration controls and automation ids, while quick-set buttons are compact secondary actions rather than large primary-looking blocks.
  - Relation groups remain usable; relation editor opens and shows input, suggestions/empty state, cancel and confirm without clipping.
  - Relation/tree remove affordance is not a permanent bright red emoji-style cross in every row; it remains keyboard/mouse/touch accessible through focus/hover/trailing action or quieter danger styling.
  - No horizontal overflow in `CurrentTaskDetailsScrollViewer` for `360`, `390`, `430`.
  - Stable automation access remains for `CurrentTaskTitleTextBox`, completion, wanted, importance, archive, id/meta, description, planning, repeater and relation controls.
  - `--ux-review task-card` generates desktop/phone screenshots and `report.json` with `Warnings: []` or documented warnings.
  - Android build/screenshot is attempted when workload and emulator/device are available; if blocked, final report names exact blocker and keeps `390x844` desktop capture as fallback only.
- Tests to add/update:
  - Extend `MainControlTaskCardLayoutUiTests` to assert compact command row still fits and key controls remain contained after visual style changes.
  - Add assertions for new/changed automation ids only if controls are replaced.
  - Keep relation editor usability assertions.
  - Keep headless `MainWindowHeadlessTests` passing.
- Visual acceptance:
  - Use `artifacts/ux-review/20260515-1732-task-card/` as before/baseline evidence.
  - Generate after screenshots under a new `artifacts/ux-review/<stamp>-task-card/`.
  - Compare at minimum:
    - `desktop/root-description.png`
    - `desktop/repeater-planning.png`
    - `desktop/blocked-relation-editor-open.png`
    - `phone/root-description-top.png`
    - `phone/repeater-planning-top.png`
    - `phone/blocked-relation-editor-open.png`
- UI video evidence:
  - If the current UI harness can safely record video for this flow, capture one phone relation-editor flow after implementation.
  - If video recording remains unsupported by repository harness, report screenshots + command output + `report.json` as fallback evidence with objective reason.
- Commands for validation:

```powershell
dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --no-progress
dotnet run --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*" --no-progress
dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --no-progress
dotnet run --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --no-progress
dotnet test .\src\Unlimotion.sln -- --no-progress
dotnet build .\src\Unlimotion.Desktop\Unlimotion.Desktop.csproj
dotnet build .\src\Unlimotion.Test\Unlimotion.Test.csproj
dotnet build .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj
dotnet run --project .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review task-card --language ru --no-build-before-launch
dotnet build .\src\Unlimotion.Android\Unlimotion.Android.csproj
```

- Stop rules for validation loops:
  - stop after targeted UI tests, full available test runs, builds and screenshots pass;
  - stop and report exact blocker if Android workload/emulator/device is unavailable;
  - stop and report exact blocker if `dotnet test .\src\Unlimotion.sln -- --no-progress` is blocked by mobile workloads or environment; run full available non-blocked test projects as next-best evidence;
  - stop and ask only if implementation reveals a product choice between materially different action/header models.

## 12. Риски и edge cases
- Risk: hiding actions in overflow reduces discoverability. Mitigation: keep `Новая` visible, keep `Еще` clearly labeled, keep desktop secondary actions visible where space allows.
- Risk: quieter delete affordance becomes hard to find. Mitigation: preserve keyboard/focus access, add tooltip/automation id, verify relation row deletion remains discoverable in review.
- Risk: planning polish accidentally changes command semantics. Mitigation: keep existing `CalendarDatePicker`, duration `TextBox`, quick-set `DropDownButton`, commands and automation ids; cover with UI smoke assertions.
- Risk: metadata becomes too hidden for power users. Mitigation: keep id visible or available via tooltip/copy affordance; do not remove created/updated entirely.
- Risk: phone screenshots look good at `390` but fail at `360`. Mitigation: keep layout tests for `360`, `390`, `430`.
- Risk: Android behavior differs due to density/keyboard. Mitigation: attempt Android validation when workload/device are available; otherwise report exact blocker.

## 13. План выполнения
1. Add/adjust targeted layout assertions for controls affected by visual hierarchy.
2. Introduce button style classes for primary, secondary, overflow and destructive actions.
3. Refactor task header metadata into muted compact rows/chips without changing bindings.
4. Lighten description section and remove duplicate placeholder treatment.
5. Polish planning layout by restyling existing controls only; do not replace them with custom tappable rows.
6. Polish relation group headers and delete affordance while preserving editor behavior.
7. Regenerate UX screenshots with `--ux-review task-card`.
8. Run targeted UI tests, headless scenarios, builds and Android build attempt.
9. Complete post-EXEC review and update journal.

## 14. Открытые вопросы
Нет блокирующих вопросов. Product decision chosen for SPEC: implement polish as shared adaptive layout, not Android-only UI fork, and preserve all existing commands.

Non-blocking follow-up outside this spec:
- A custom phone planning row component may be designed later if product wants a stronger mobile interaction model; this spec deliberately keeps the existing controls.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`; контексты `testing-dotnet`, `visual-feedback`.
- Выполненные требования профиля:
  - UI-facing change has visual planning artifact in this spec.
  - UI automation coverage is planned through existing Avalonia.Headless/AppAutomation patterns.
  - Visual evidence is mandatory through existing real-app screenshot capture.
  - Stable automation selectors are preserved unless explicitly superseded.
  - Android/narrow behavior is explicitly validated or reported with environment caveat.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-05-15-task-card-visual-polish.md` | Новая спека | Зафиксировать следующий UX/UI polish этап |
| `src/Unlimotion/Views/MainControl.axaml` | Button hierarchy, header/meta, description, planning and relations visual polish | Сделать карточку удобнее and visually calmer |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Optional scoped responsive helper updates | Только если XAML styles cannot express needed behavior |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | Optional labels/tooltips | New visible strings if needed |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | Optional labels/tooltips | Russian localization |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Updated compact layout/hierarchy assertions | Catch polish regressions |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Selector updates if controls are replaced | Preserve automation access |
| `tests/Unlimotion.ReadmeMedia/Program.cs` | Optional new capture state only if needed | Keep visual evidence complete |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Command bar | Many grey actions compete | Clear primary, secondary, overflow and destructive hierarchy |
| Header | Title plus raw id/dates/actions in one noisy area | Title-first header with compact state chips and muted metadata |
| Description | Section title plus duplicate inner placeholder | Lighter editor without duplicate label treatment |
| Planning phone | Long form of field + large button pairs | Same controls with lighter spacing and secondary quick action treatment |
| Relations | Usable but visually noisy groups and red deletes | Same model, quieter group headers and less dominant delete affordance |
| Visual acceptance | Mostly no-overflow evidence | No-overflow plus hierarchy/readability checklist |
| Android | Fallback `390x844` only when SDK blocked | Same fallback rule, explicit non-equivalence to real Android evidence |

## 18. Альтернативы и компромиссы
- Вариант: leave current layout and only tweak colors.
- Плюсы: lowest implementation risk.
- Минусы: does not solve header/planning/relations hierarchy.
- Почему выбранное решение лучше: targeted structure polish addresses actual screenshot findings while keeping scope UI-only.

- Вариант: fully redesign card into new component.
- Плюсы: cleanest long-term UI composition.
- Минусы: higher regression risk, selector churn, more tests and review cost.
- Почему выбранное решение лучше: current card is already functional; incremental polish gives most user value with less behavioral risk.

- Вариант: collapse all relations by default on phone.
- Плюсы: shorter card.
- Минусы: hides a strong part of current implementation and changes discoverability.
- Почему выбранное решение лучше: make groups quieter first; collapse can be a later product decision if evidence still shows excessive length.

- Вариант: replace phone planning controls with custom tappable rows.
- Плюсы: visually cleaner and more mobile-native.
- Минусы: changes keyboard editing, automation ids and date/duration interaction contract.
- Почему выбранное решение лучше: current spec is polish-only; existing controls are restyled to reduce weight without changing behavior.

- Вариант: remove metadata from header entirely.
- Плюсы: cleanest title area.
- Минусы: power users lose task id/date visibility.
- Почему выбранное решение лучше: muted metadata preserves information without dominating.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели and Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Responsibilities, detailed UI design, integration, state and rollback described. |
| C. Безопасность изменений | 11-13 | PASS | UI-only scope, no data migration, risks and rollback covered. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, targeted/full tests, screenshots and commands defined. |
| E. Готовность к автономной реализации | 17-19 | PASS | Mapping, alternatives and planning fallback are documented without EXEC scope creep. |
| F. Соответствие профилю | 20 | PASS | .NET desktop, UI automation and visual feedback requirements reflected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope is specific: visual hierarchy polish for existing task card, no domain changes. |
| 2. Понимание текущего состояния | 5 | Uses concrete screenshot evidence, files and known Android blocker. |
| 3. Конкретность целевого дизайна | 5 | Includes target wireframes, button/header/planning/relation rules; planning control replacement is explicitly out of scope. |
| 4. Безопасность (миграция, откат) | 5 | UI-only changes, rollback and compatibility are explicit. |
| 5. Тестируемость | 5 | Defines targeted UI tests, full available test runs, builds and visual evidence command. |
| 6. Готовность к автономной реализации | 5 | No blocking open questions; fallback choices are defined. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS after review fixes
- Scope reviewed: `specs/2026-05-15-task-card-visual-polish.md`, instruction stack `model-behavior-baseline + quest-governance + collaboration-baseline + testing-baseline + testing-dotnet + visual-feedback + dotnet-desktop-client + ui-automation-testing + quest-mode + spec-linter + spec-rubric + review-loops + AGENTS.override.md`, selected profile, open questions, planned changed files, UX evidence `artifacts/ux-review/20260515-1732-task-card/`.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: checked current screenshots, previous follow-up spec, current changed-file surface and repository UI-test constraints.
  - Contract pass: spec stays UI-only, preserves commands/bindings/storage, requires targeted/full tests and screenshot evidence.
  - Adversarial risk pass: checked risks around hidden actions, delete discoverability, planning control replacement, relation counts scope, metadata hiding and Android fallback.
  - Re-review after fixes / Fix and re-review: fixed validation commands, relation counts scope, planning contract, objective acceptance criteria and date visibility; rechecked affected sections.
  - Stop decision: PASS; no blocking open questions.
- Evidence inspected: `report.json`, desktop/phone screenshots from `artifacts/ux-review/20260515-1732-task-card/`, `src/Unlimotion/Views/MainControl.axaml` style/control locations, `MainControlTaskCardLayoutUiTests` assertions.
- Depth checklist:
  - Scope drift / unrelated changes: spec-only phase; implementation files listed but not modified by this spec creation.
  - Acceptance criteria: measurable through UI tests and screenshot comparison; subjective wording replaced with concrete visible-state checks.
  - Validation evidence: planned commands include targeted tests, full available test runs, headless tests, builds, screenshot capture and Android build attempt.
  - Unsupported claims: claims tied to current screenshots and known command/test files.
  - Regression / edge case: covers phone widths, relation editor, delete discoverability, visible metadata and planning command semantics.
  - Comments/docs/changelog: no code comments planned unless needed; spec itself is the documentation artifact.
  - Hidden contract change: explicitly forbids changing command semantics, storage, relation behavior and planning control replacement.
  - Manual-review challenge: likely manual concern is whether visual criteria are too subjective; acceptance now names concrete UI states to inspect.
- No-findings justification: Не применимо; review findings were found and fixed/accepted below.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | validation | Validation commands lacked a full available test run required by central `testing-dotnet` / `dotnet-desktop-client` guidance | Add full `Unlimotion.Test` and full headless UI test commands; require exact blocker report for broader solution/mobile workload issues | fixed |
| MEDIUM | scope | Relation counts wording allowed EXEC-time scope creep into ViewModel/domain changes | Move relation counts fully out of this spec | fixed |
| MEDIUM | planning | Phone planning row concept could imply replacing current controls and changing keyboard/automation behavior | Lock this spec to visual polish of existing `CalendarDatePicker`, duration `TextBox`, quick-set `DropDownButton` and automation ids | fixed |
| MEDIUM | acceptance | Several UX criteria were subjective (`visibly secondary`, `visually lighter`, `less dominant`) | Replace with concrete screenshot-checkable states for primary accent, muted metadata, compact planning controls and delete affordance | fixed |
| LOW | metadata | Dates could move into a hidden flyout/details surface without an explicit UX contract | Keep present dates visible in muted/wrapped metadata area; no new hidden date surface in this spec | fixed |

- Fixed before continuing: full available test commands added; relation counts excluded; planning control replacement excluded; acceptance criteria made objective; dates kept visible.
- Checks rerun: SPEC linter/rubric/review loop completed again in this file after fixes.
- Needs human: confirmation phrase before EXEC.
- Residual risks / follow-ups: Real Android evidence still depends on local workload/emulator availability.

### Post-EXEC Review
- Статус: PASS with documented validation blockers outside the accepted visual-polish scope
- Scope reviewed: `git status --short --branch`, `git diff --stat`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`, UX screenshots `artifacts/ux-review/20260516-0106-task-card/`, `report.json`, validation command output.
- Decision: EXEC завершён; изменения соответствуют утверждённой spec. Full suite/mobile blockers зафиксированы ниже and require separate triage/environment setup.
- Review passes:
  - Scope/Evidence pass: checked changed XAML/test/selectors only; generated screenshots cover desktop and phone for root, planning/repeater and relation editor states.
  - Contract pass: storage/domain command semantics unchanged; existing command bindings and relation editor model preserved; new selectors/classes only support visual hierarchy and tests.
  - Adversarial risk pass: checked phone density, muted metadata, destructive affordance discoverability, relation editor usability and no horizontal clipping evidence.
  - Re-review after fixes / Fix and re-review: reran targeted layout and relation editor tests after implementation; reviewed final screenshots and `report.json`.
  - Stop decision: PASS for implemented scope; stop with documented blockers for Android workload and broader full-suite failures.
- Evidence inspected: `artifacts/ux-review/20260516-0106-task-card/report.json`, `desktop/root-description.png`, `desktop/repeater-planning.png`, `desktop/blocked-relation-editor-open.png`, `phone/root-description-top.png`, `phone/repeater-planning-top.png`, `phone/blocked-relation-editor-open.png`.
- Depth checklist:
  - Scope drift / unrelated changes: no model/storage/command changes added for this spec; existing broader changed files from previous task-card work remain in the worktree.
  - Acceptance criteria: primary/secondary/destructive classes are asserted; phone compact layout remains covered for `360/390/430`; screenshots show no horizontal clipping and `Warnings: []`.
  - Validation evidence: targeted layout tests, relation editor tests, targeted headless tests, desktop/test builds, ReadmeMedia build, screenshot capture and diff check completed; broader/full/mobile blockers documented.
  - Unsupported claims: visual claims are tied to regenerated screenshots and report file.
  - Regression / edge case: relation editor open state checked on desktop and phone; full headless uncovered a broader thread-affinity failure in relation/delete flow that is not resolved by this visual-polish spec.
  - Comments/docs/changelog: no code comments required; spec journal and post-EXEC evidence updated.
  - Hidden contract change: no replacement of planning controls; `CalendarDatePicker`, duration `TextBox`, quick-set `DropDownButton`, relation add/remove commands remain.
  - Manual-review challenge: phone importance control is still dense; acceptable for this polish pass, but worth separate follow-up if a more mobile-native numeric editor is desired.
- No-findings justification: targeted changed surface passed; remaining issues are validation blockers/follow-ups, not required code fixes in the approved scope.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | full headless validation | `dotnet run --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --no-progress` fails in a broader relation/delete scenario with `InvalidOperationException: The calling thread cannot access this object because a different thread owns it`, stack through `TaskItemViewModel.SynchronizeTaskCollection` and `UnifiedTaskStorage.Delete` | Separate triage of storage/relation update thread affinity; not fixed in this visual-polish spec | follow-up |
| MEDIUM | full unit/ui validation | `dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --no-progress` did not finish after the initial 184s timeout plus 300s wait; the orphaned `dotnet test` process was stopped | Investigate full-suite hang separately; targeted affected tests passed | blocked |
| MEDIUM | Android / solution validation | `dotnet test .\src\Unlimotion.sln -- --no-progress` and `dotnet build .\src\Unlimotion.Android\Unlimotion.Android.csproj` are blocked by `NETSDK1147` missing workload `wasm-tools` | Run `dotnet workload restore` / install required workloads, then rerun Android and solution validation | blocked |
| LOW | visual polish follow-up | Phone state row remains dense around importance numeric input; no clipping observed, but it is not yet mobile-native | Consider a separate compact importance editor/chip spec if desired | follow-up |

- Fixed before final report: visual hierarchy classes, compact header/meta rows, description placeholder duplication, planning quick-action styling, relation add/delete visual treatment and automation selectors.
- Checks rerun:
  - PASS `dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --no-progress` -> 4 passed.
  - PASS `dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlRelationPickerUiTests/*" --no-progress` -> 5 passed.
  - PASS `dotnet run --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*" --no-progress` -> 8 passed.
  - PASS `dotnet build .\src\Unlimotion.Desktop\Unlimotion.Desktop.csproj`.
  - PASS `dotnet build .\src\Unlimotion.Test\Unlimotion.Test.csproj`.
  - PASS `dotnet build .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj`.
  - PASS `dotnet run --project .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review task-card --language ru --no-build-before-launch` -> screenshots in `artifacts/ux-review/20260516-0106-task-card/`, `Warnings: []`.
  - PASS `git diff --check` (line-ending warnings only).
  - BLOCKED/TIMEOUT `dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --no-progress`.
  - FAIL/BLOCKED `dotnet run --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --no-progress` due thread-affinity exception above.
  - FAIL/BLOCKED `dotnet test .\src\Unlimotion.sln -- --no-progress` due missing `wasm-tools`.
  - FAIL/BLOCKED `dotnet build .\src\Unlimotion.Android\Unlimotion.Android.csproj` due missing `wasm-tools`.
- Validation evidence: UX screenshots and `report.json` under `artifacts/ux-review/20260516-0106-task-card/`; command results above.
- Unrelated changes: Worktree already contains previous task-card/mobile-follow-up changes in `MainControl.axaml.cs`, `Strings.resx`, `Strings.ru.resx`, AppAutomation host/data and ReadmeMedia capture tooling; they were preserved and not reverted.
- Needs human: review final screenshots if product wants to tune visual density further; install Android/mobile workloads for real Android validation.
- Residual risks / follow-ups: full suite hang, full headless relation/delete thread-affinity failure, real Android screenshot validation after workload/device availability, optional mobile-native importance editor.

## Approval
Получено: "спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction stack and UX evidence review | 0.93 | Нет | Создать spec | Нет | Да, пользователь попросил составить spec | Центральный QUEST stack and local UI-test override require spec-first; current screenshot evidence provides concrete findings | `C:\Users\Kibnet\.codex\agents\AGENTS.md`, central instruction stack, `artifacts/ux-review/20260515-1732-task-card/*`, `specs/2026-05-15-task-card-mobile-ux-follow-up.md` |
| SPEC | New visual polish spec creation | 0.90 | Real Android evidence availability | Запросить подтверждение | Да | Нет | Recommendations converted into bounded UI-only polish with acceptance criteria, visual artifact, validation commands and rollback | `specs/2026-05-15-task-card-visual-polish.md` |
| SPEC | Review fixes | 0.94 | Real Android evidence availability | Передать исправленную spec на подтверждение | Да | Да, пользователь попросил поправить review findings | Закрыты findings по full validation, relation counts scope, planning contract, objective acceptance criteria and visible metadata | `specs/2026-05-15-task-card-visual-polish.md` |
| EXEC | Spec approval intake | 0.96 | Нет | Реализовать утверждённый visual polish | Нет | Да, пользователь подтвердил spec | Подтверждение сняло QUEST stop rule; можно менять implementation files | `specs/2026-05-15-task-card-visual-polish.md` |
| EXEC | Task card visual hierarchy implementation | 0.88 | Нет | Обновить targeted UI assertions | Нет | Нет | Styles/classes introduce primary/secondary/destructive hierarchy, quieter header metadata, lighter description/planning and relation row actions without changing command semantics | `src/Unlimotion/Views/MainControl.axaml` |
| EXEC | UI selector and layout test update | 0.90 | Нет | Сгенерировать screenshots and run validation | Нет | Нет | Tests now assert hierarchy-critical classes, compact mode visibility and stable planning quick-action automation ids | `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` |
| EXEC | UX screenshot capture and visual review | 0.88 | Real Android screenshots unavailable until workload/device setup | Run validation commands | Нет | Нет | Desktop/phone screenshots show no horizontal clipping and improved visual hierarchy; phone importance input remains dense but acceptable | `artifacts/ux-review/20260516-0106-task-card/*` |
| EXEC | Validation and post-EXEC review | 0.84 | Full-suite hang root cause, full headless relation/delete root cause, Android workload | Передать результат пользователю | Нет | Нет | Targeted changed surface passes; broader blockers are documented with exact commands/errors for separate triage | `specs/2026-05-15-task-card-visual-polish.md` |
