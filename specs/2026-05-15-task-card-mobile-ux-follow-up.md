# Task Card Mobile UX Follow-Up and Screenshot Review Protocol

## 0. Метаданные
- Тип (профиль): `delivery-task`; профили `dotnet-desktop-client`, `ui-automation-testing`; контексты `testing-dotnet`, `visual-feedback`.
- Владелец: Codex / пользователь.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: follow-up к текущему редизайну карточки задачи; отдельная ветка или текущая PR-ветка до merge по решению владельца.
- Ограничения: QUEST mode; до подтверждения менять только эту спецификацию; UI-facing изменение обязано иметь UI tests, visual evidence и релевантные проверки; Android/narrow width является first-class target.
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `C:\Users\Kibnet\.codex\agents\instructions\core\quest-mode.md`, `C:\Users\Kibnet\.codex\agents\instructions\contexts\visual-feedback.md`, `specs/2026-05-14-task-card-redesign.md`, baseline screenshots `artifacts/task-card-redesign-screenshots/*.png`.

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Доработать карточку задачи после первого редизайна: привести desktop к более плотному и сканируемому рабочему виду, а узкий Android/phone layout сделать реально usable без горизонтального клиппинга, с компактными действиями и устойчивым one-column потоком. Одновременно закрепить повторяемый протокол скриншотов для будущих UX/UI review, чтобы визуальные решения проверялись не только layout-тестами, но и реальными артефактами окна.

Outcome contract:
- Success means: на desktop карточка выглядит как плотная рабочая панель без лишней визуальной тяжести; на ширине `360-430` logical pixels пользователь видит понятную карточку в одну колонку без обрезанного текста и без горизонтального скролла; команда может воспроизводимо снимать desktop and phone screenshots для UX/UI review.
- Итоговый артефакт / output: XAML/style/layout правки карточки, компактный mobile command model, UI tests для narrow layout, reusable screenshot capture mode/instruction, updated spec journal and review evidence.
- Stop rules: остановиться после выполнения acceptance criteria, targeted UI tests, desktop/test builds, screenshot capture evidence, post-EXEC review, или после явного отчёта о невозможности Android/emulator validation.

## 2. Текущее состояние (AS-IS)
- Первый редизайн карточки реализован в `src/Unlimotion/Views/MainControl.axaml` и покрыт `MainControlTaskCardLayoutUiTests`, AppAutomation page selectors and headless scenarios.
- Android использует общий Avalonia UI из `src/Unlimotion/Unlimotion.csproj`; отдельной Android-only карточки нет.
- Свежие screenshots из UX review лежат в `artifacts/task-card-redesign-screenshots/`:
  - `desktop-root-description.png`
  - `desktop-repeater-planning.png`
  - `desktop-blocked-relation.png`
  - `phone-root-description.png`
  - `phone-repeater-planning.png`
  - `phone-blocked-relation.png`
- Desktop result:
  - карточка структурно стала лучше: header, description, planning, repeater and relations отделены;
  - верхний command bar всё ещё очень крупный;
  - правый details pane визуально конкурирует с task tree;
  - поля id/date/action занимают много ширины и высоты;
  - нижние секции доступны, но воспринимаются как длинная форма.
- Phone/narrow result:
  - первый экран вытесняется command bar и верхними кнопками;
  - до карточки нужно прокручивать, и это выглядит как побочный эффект desktop layout;
  - id клиппится слева (`launch-pilot` отображается как `unch-pilot`, `publish-landing` как `blish-landing`);
  - часть title/header context скрыта или обрезана;
  - layout тесты ловят отсутствие горизонтального overflow только частично, но screenshots показывают UX-проблему.
- Screenshot capture observation:
  - прямой `RenderTargetBitmap`/unit-test capture давал пустые или чёрные PNG;
  - рабочий evidence получился через реальный desktop запуск и FlaUI/Win32 capture, аналогично `tests/Unlimotion.ReadmeMedia`.

## 3. Проблема
Одна корневая проблема: текущая карточка стала структурной, но всё ещё наследует desktop-first компоновку; на узком экране она технически отображается, но пользовательский сценарий редактирования задачи остаётся неудобным и визуально нестабильным.

## 4. Цели дизайна
- Mobile-first для details pane: ширина `360-430` должна быть полноценным layout target, а не fallback.
- Сохранить текущие сильные relation-блоки: четыре группы, inline add editor, suggestions, confirm/cancel and tree.
- Снизить визуальный вес desktop command bar and task header without hiding core task state.
- Сделать id/dates вторичными метаданными, не конкурирующими с title and actions.
- Устранить горизонтальное обрезание текста and controls на narrow width.
- Закрепить воспроизводимый screenshot protocol для каждого будущего UX/UI review.
- Сохранить bindings, commands, `AutomationId`, persisted task schema and existing tests unless a selector is explicitly superseded.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем доменную модель задачи, storage schema, sync/backup logic, sorting/filtering and task tree behavior.
- Не заменяем relation editor новым simplified design.
- Не делаем отдельный Android-only UI до доказанного провала shared adaptive layout.
- Не добавляем новую глобальную дизайн-систему.
- Не добавляем новые relation count fields/computations; counts остаются вне scope, если они уже не видны в текущем UI.
- Не добавляем визуальные эффекты, marketing layout, nested cards or decorative backgrounds.
- Не коммитим screenshot artifacts в репозиторий, если команда отдельно не решит хранить golden images.
- Не используем headless `RenderTargetBitmap` как единственный visual evidence для UX review, пока он не доказан стабильным на этой платформе.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> responsive task card layout, compact command bar, mobile one-column flow, section spacing and wrapping rules.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> только при необходимости: scoped width-class переключатель для `TaskDetailsCompact` / `TaskDetailsRegular`; без изменения task state.
- `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` -> selectors for any new stable controls, если они добавляются.
- `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` -> smoke coverage for compact command/card access, relation picker preservation.
- `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` -> expand phone-width assertions: no clipping, key controls fully within details scroll viewport, no horizontal overflow.
- `tests/Unlimotion.ReadmeMedia/Program.cs` or отдельный близкий capture utility -> добавить reusable UX screenshot mode, не одноразовый patch.
- `specs/2026-05-15-task-card-mobile-ux-follow-up.md` -> UX decisions, screenshot protocol, evidence rules and execution journal.

### 6.2 Детальный дизайн
#### Desktop polish
- Command bar:
  - уменьшить визуальную высоту кнопок and padding;
  - оставить все desktop actions visible, но разрешить wrap without large vertical gaps;
  - `Удалить` визуально отделить от creation actions, чтобы destructive action не выглядела как рядовая command tile.
- Card header:
  - title remains primary and full-width within right pane;
  - completion checkbox stays near title;
  - wanted, importance and archive move into compact secondary action/meta row;
  - raw id becomes secondary meta field, with trim/copy affordance if needed.
- Dates:
  - desktop can keep inline meta row, but labels must not create a long noisy sentence;
  - completed/unlocked/archive dates show only when present;
  - on empty fields use absence, not disabled-looking placeholders that read as unavailable controls.
- Description/planning/repeater:
  - keep section headers;
  - reduce excessive vertical gaps;
  - planning fields align as a group but must still wrap cleanly.
- Relations:
  - preserve existing groups and inline editor;
  - do not add new compact relation counts in this follow-up; relation counts are allowed only if already present in existing UI without new ViewModel/computed fields;
  - keep relation controls visually subordinate to task identity and planning.

#### Phone / Android narrow layout
Target widths: `360`, `390`, `430` logical pixels.

```text
Details pane, compact mode
┌──────────────────────────────┐
│ compact command row          │
│ [Новая] [Еще]                │
│ optional second row only if   │
│ there is room                 │
├──────────────────────────────┤
│ Task card                    │
│ [ ] Title editor             │
│ Wanted  Importance  Archive  │
│ id / dates as wrapped meta   │
│                              │
│ Описание                     │
│ full-width textbox           │
│                              │
│ Планирование                 │
│ begin                        │
│ set begin                    │
│ duration                     │
│ set duration                 │
│ end                          │
│ set end                      │
│                              │
│ Повторение                   │
│ full-width controls          │
│ weekday toggles wrap         │
│                              │
│ Связи                        │
│ Parents [+]                  │
│ inline editor/tree           │
│ Blocking [+]                 │
│ inline editor/tree           │
│ Containing [+]               │
│ inline editor/tree           │
│ Blocked [+]                  │
│ inline editor/tree           │
└──────────────────────────────┘
```

Rules:
- No horizontal clipping at `390x844`.
- No horizontal scrollbar for task card content.
- On `390x844`, the first phone viewport after opening details must show the compact command row and the beginning of the card header/title without scrolling; acceptance fails if the card header is only reachable after scroll.
- Inputs use full available width on compact mode.
- Any raw id field must have `MinWidth=0`, wrapping/trimming, or be replaced by a compact display + copy action.
- Date labels stack as label/value pairs on phone; long inline metadata sentence is not allowed.
- Buttons keep touch-friendly height around `40-44` logical pixels.
- Relation add editor stacks vertically: input, suggestions/empty state, buttons.
- If split pane/tree conflicts with details pane on phone, details mode may become full-screen/overlay while open, but tree behavior outside details must remain unchanged.

#### Screenshot protocol v1
The repository should have a repeatable UX capture flow. The implementation target is a reusable command, not an ad hoc temporary test.

Preferred command to add:

```powershell
dotnet run --project .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- `
  --ux-review task-card `
  --language ru `
  --output-root .\artifacts\ux-review\<yyyyMMdd-HHmm>-task-card
```

Required output structure:

```text
artifacts/ux-review/<stamp>-task-card/
  report.json
  desktop/
    root-description.png
    repeater-planning.png
    blocked-relation.png
    blocked-relation-editor-open.png
  phone/
    root-description-top.png
    root-description-card.png
    repeater-planning-top.png
    repeater-planning-card.png
    blocked-relation-top.png
    blocked-relation-card.png
    blocked-relation-editor-open.png
```

Required capture cases:
- `root-description`: task with clear title, description, no special blocker emphasis; baseline example `launch-pilot`.
- `repeater-planning`: task with planned begin/end/duration and repeater; baseline example `capture-readme-tour`.
- `blocked-relation`: task with blocking/blocked relation and planning; baseline example `publish-landing`.
- `blocked-relation-editor-open`: same blocked relation task with existing relation add editor opened through the real UI, including input, suggestions/empty state, confirm and cancel controls.

Required viewports:
- Desktop: real application window, prefer existing `ResizeDesktopWindow` behavior from README media because it fits current monitor and captures realistic chrome.
- Phone/narrow: exact `390x844` window for shared Avalonia layout fallback.
- Android evidence: MUST attempt emulator/device screenshot when Android workload and emulator/device are available. If unavailable, report the exact blocker and mark `390x844` desktop capture as fallback evidence, not equivalent Android evidence.

Capture method:
- Use real launched app through `Unlimotion.AppAutomation.TestHost` and FlaUI/Win32 capture, matching the successful README media pipeline.
- Before capture, resolve and record exact `MainWindowTitle`; capture only the active, non-minimized window.
- Do not rely on unit-test `RenderTargetBitmap` for UX evidence unless it is first proven to produce nonblank representative images.
- For phone cases, capture both:
  - `top`: first viewport after opening details;
  - `card`: viewport after scrolling/focusing to the card header or main editable fields.
- The `phone/*-card.png` screenshots may use one intentional scroll/focus to inspect fields, but they do not satisfy first-viewport acceptance.
- For `blocked-relation-editor-open`, open the relation add editor using the same command/button a user would use, then capture the open editor in desktop and phone width.
- Record in `report.json`: branch, commit SHA, timestamp, exact window title, language, viewport sizes, task ids, capture command, OS/display scale if available, and warnings.
- After capture, verify `git status --short` has no temporary capture code left unless the reusable capture command is the intended change.

UX review checklist for every capture set:
- No blank/black screenshots.
- No horizontal text clipping in phone screenshots.
- At `390x844`, first phone viewport shows compact commands plus the beginning of card header/title without scrolling.
- Command bar does not dominate phone viewport.
- Title, completion, wanted, importance and archive are visible and understandable.
- Dates/id do not crowd title or action controls.
- Description, planning, repeater and relations appear in predictable order.
- Relation editor can still be opened and remains usable at phone width.
- Relation editor screenshots show input, suggestions/empty state and confirm/cancel controls without clipping.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Не применимо: бизнес-логика задач не меняется.
- UI invariants:
  - existing task commands keep their current commands and effects;
  - relation add/edit behavior remains identical;
  - hidden/visible state of repeater and date-related controls remains data-driven as before;
  - screenshot capture must not mutate persisted user data outside the isolated automation scenario.

## 8. Точки интеграции и триггеры
- `MainControl` renders compact mode when details pane width falls below the selected breakpoint.
- `Unlimotion.AppAutomation.TestHost` provides deterministic demo tasks for screenshot capture.
- `Unlimotion.ReadmeMedia` or a sibling utility owns reusable screenshot capture.
- AppAutomation/FlaUI and Avalonia.Headless tests validate selectors, relation flows and layout bounds.
- Android build/smoke validation is attempted after shared layout changes if the local workload exists.

## 9. Изменения модели данных / состояния
- Persisted task model: no changes.
- ViewModel API: no required changes.
- UI state: optional width class/state for compact layout only.
- Screenshot report: new generated artifact under `artifacts/`, not persisted app state.

## 10. Миграция / Rollout / Rollback
- Миграция: не требуется.
- Rollout: ship as UI-only follow-up to task card redesign.
- Rollback:
  - revert compact layout changes in `MainControl.axaml` / optional code-behind;
  - remove or disable the UX screenshot capture command if it causes maintenance cost;
  - keep screenshot protocol in specs as review guidance unless superseded by a dedicated docs page.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Phone screenshots at `390x844` show no clipped id/title/date text inside task card.
  - At `390x844`, first phone viewport after opening details shows compact commands plus the beginning of the card header/title without scrolling.
  - Command bar no longer consumes most of the first phone viewport.
  - Desktop screenshots show a denser command/header area and clearer task/card hierarchy.
  - Relation blocks keep the current interaction model and remain usable on phone width.
  - Open relation add editor remains usable on desktop and phone width, with screenshot evidence for both.
  - No horizontal overflow in `CurrentTaskDetailsScrollViewer` for `360`, `390`, `430` widths.
  - `CurrentTaskTitleTextBox`, completion checkbox, wanted checkbox, importance, archive, id, description, planning and relation controls keep stable automation access.
  - Reusable UX capture command generates required screenshots and `report.json`.
  - Temporary capture code is not left in the repository unless it is the reusable command itself.
  - Android emulator/device screenshot is attempted when workload and emulator/device are available; if blocked, the final report names the blocker and treats `390x844` desktop capture only as fallback evidence.
- Tests to add/update:
  - Extend `MainControlTaskCardLayoutUiTests` with phone widths `360`, `390`, `430`.
  - Add assertion for full containment of id/title/date controls in phone layout, not only non-zero bounds.
  - Keep AppAutomation relation picker tests passing.
  - Add smoke coverage for reusable screenshot capture command if feasible as a fast argument parsing/output test.
- Visual acceptance:
  - Compare generated screenshots to the wireframe and checklist in section 6.2.
  - Store generated evidence under `artifacts/ux-review/<stamp>-task-card/`.
- UI video evidence:
  - If the UI harness/runner can safely record video for this flow, capture at least one `phone-card` flow after implementation.
  - If video recording is not supported or would require unstable tooling, report screenshots + commands as fallback evidence and explicitly state the objective reason.
- Commands for validation:

```powershell
dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --no-progress
dotnet run --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*" --no-progress
dotnet build .\src\Unlimotion.Desktop\Unlimotion.Desktop.csproj
dotnet build .\src\Unlimotion.Test\Unlimotion.Test.csproj
dotnet run --project .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review task-card --language ru --output-root .\artifacts\ux-review\<stamp>-task-card
dotnet build .\src\Unlimotion.Android\Unlimotion.Android.csproj
```

- Android build command may be blocked by missing workload. If blocked, report the exact SDK message and keep narrow desktop screenshots as fallback evidence.
- Android screenshot capture may be blocked by missing workload, emulator or physical device. If blocked, report the exact reason; do not describe desktop `390x844` evidence as equivalent to Android validation.
- Stop rules for validation loops:
  - stop after all targeted UI tests and screenshots pass;
  - stop and report if Android workload is unavailable;
  - stop and ask only if a product-level choice is required between materially different mobile command models.

## 12. Риски и edge cases
- Risk: compact command menu hides actions users expect. Mitigation: keep primary `Новая` visible and put secondary actions into a clearly labeled `Еще`/overflow; verify discoverability in screenshots.
- Risk: width-class code-behind becomes hidden state. Mitigation: prefer XAML wrapping; if code-behind is needed, scope it to visual classes and test breakpoints.
- Risk: screenshot command becomes slow/flaky because it launches real app several times. Mitigation: build once per process, deterministic automation data, report warnings.
- Risk: exact `390x844` desktop window does not fully represent Android density/keyboard. Mitigation: use it only as fallback, attempt emulator/device screenshots when workload and device/emulator are available, and report the exact blocker otherwise.
- Risk: relation section grows too long on phone. Mitigation: preserve flow but allow vertical stacking and consider collapsible relation groups only as a separate product decision.

## 13. План выполнения
1. Add/adjust narrow layout tests first for `360`, `390`, `430` widths and current clipping cases.
2. Refactor command bar into regular and compact presentation without changing commands.
3. Refactor task header/meta/actions into explicit desktop and compact wrapping rules.
4. Fix id/date clipping by changing min widths, wrapping/trimming and phone meta layout.
5. Verify description, planning, repeater and relation sections in compact one-column flow.
6. Add reusable UX screenshot capture command based on real desktop/FlaUI pipeline.
7. Generate before/after screenshot evidence using the protocol.
8. Run targeted UI tests/builds and Android build if workload is available.
9. Complete post-EXEC review and update the journal.

## 14. Открытые вопросы
Нет блокирующих вопросов. Product decision chosen for SPEC: compact mobile command model should keep `Новая` visible and move secondary actions into an overflow/menu because this directly reduces first-viewport clutter while preserving access to existing actions.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`; контексты `testing-dotnet`, `visual-feedback`.
- Выполненные требования профиля:
  - UI-facing change has planned UI automation coverage.
  - Visual evidence is mandatory and reproducible.
  - Android/narrow behavior is explicitly tested or reported with environment caveat.
  - Existing selectors and relation flows are preserved unless explicitly updated.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-05-15-task-card-mobile-ux-follow-up.md` | Новая спека | Зафиксировать follow-up UX changes and screenshot protocol |
| `src/Unlimotion/Views/MainControl.axaml` | Compact command bar, phone one-column card, desktop density polish | Устранить narrow UX issues |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Optional scoped compact class by width | Только если XAML wrapping недостаточен |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | New selectors if needed | Stable automation access |
| `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` | Preserve relation flow and card access checks | UI regression coverage |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Stronger phone-width bounds/no-clipping tests | Catch mobile layout regressions |
| `tests/Unlimotion.ReadmeMedia/Program.cs` or sibling utility | Reusable `--ux-review task-card` capture mode | Repeatable UX/UI screenshot evidence |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Desktop command bar | Large button row consumes significant height | Compact row with clearer primary/secondary/destructive grouping |
| Desktop card | Structured but still visually heavy | Denser header/meta/actions, clearer hierarchy |
| Phone first viewport | Mostly command buttons, card begins below fold | Compact commands plus beginning of card header/title visible without scroll at `390x844` |
| Phone id/meta | Raw id clips horizontally | Meta wraps/trims/copies without clipping |
| Phone planning | Desktop groups squeeze into narrow width | One-column field/action flow |
| Relations | Strong existing four-group pattern | Same pattern preserved, adapted vertically on phone without new relation counts |
| Relation editor evidence | Editor usability inferred from relation section screenshots | Open relation editor captured separately on desktop and phone |
| UX screenshots | Ad hoc capture, temporary code, headless bitmap failed | Reusable real-app capture protocol with report, checklist and mandatory Android attempt when environment allows |

## 18. Альтернативы и компромиссы
- Вариант: keep all mobile actions visible as wrapped buttons.
- Плюсы: maximum discoverability.
- Минусы: first viewport remains dominated by commands.
- Почему выбранное решение лучше: primary + overflow preserves access while making the card visible.

- Вариант: create Android-only task card.
- Плюсы: could optimize mobile independently.
- Минусы: duplicate bindings, actions and tests; higher divergence risk.
- Почему выбранное решение лучше: shared adaptive Avalonia layout fits current architecture and reduces maintenance.

- Вариант: collapse relation groups by default on phone.
- Плюсы: shorter card.
- Минусы: changes relation discoverability and behavior expectations.
- Почему выбранное решение лучше: preserve relation model now; collapsible relations can be a separate product decision if screenshots still show excessive length.

- Вариант: use headless `RenderTargetBitmap` for screenshots.
- Плюсы: simpler in tests.
- Минусы: produced blank/black/nonrepresentative images in current environment.
- Почему выбранное решение лучше: real desktop/FlaUI capture already works and matches user-visible UI.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели and Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Desktop/mobile design, screenshot protocol, integration and rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Данные не меняются; rollout, rollback and risks covered. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, tests, visual evidence and commands defined. |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan, alternatives and no blocking questions documented. |
| F. Соответствие профилю | 20 | PASS | .NET desktop, UI automation and visual feedback requirements reflected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Follow-up scope is specific: mobile UX, desktop polish, screenshot protocol. |
| 2. Понимание текущего состояния | 5 | Uses concrete screenshot findings, files and test/capture constraints. |
| 3. Конкретность целевого дизайна | 5 | Includes desktop rules, phone wireframe and capture protocol. |
| 4. Безопасность (миграция, откат) | 5 | UI-only, no persisted state change, rollback described. |
| 5. Тестируемость | 5 | Defines UI tests, visual evidence, commands and fallback. |
| 6. Готовность к автономной реализации | 5 | No blocking open questions; selected tradeoffs are explicit. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-05-15-task-card-mobile-ux-follow-up.md`, refreshed central instruction stack `model-behavior-baseline + quest-governance + collaboration-baseline + testing-baseline + testing-dotnet + visual-feedback + dotnet-desktop-client + ui-automation-testing + quest-mode + spec-linter + spec-rubric + review-loops + AGENTS.override.md`, selected profiles, open questions, planned changed files.
- Decision: можно запрашивать подтверждение.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | mobile acceptance | First-phone-viewport criterion allowed minimal scroll in one place and no-scroll visibility in another | Require compact commands plus beginning of card header/title in first `390x844` viewport without scroll; keep scrolled `phone/*-card.png` only for field inspection | fixed |
| MEDIUM | relation evidence | Required screenshot set did not prove that relation add editor still works on phone | Add desktop and phone `blocked-relation-editor-open.png` artifacts and capture instructions | fixed |
| LOW | scope | Relation counts wording could be read as permission to add new computed ViewModel fields | Move new relation counts to Non-Goals and allow counts only if already visible in current UI | fixed |
| LOW | Android evidence | Android screenshot evidence was optional and fallback wording could overstate desktop narrow capture | Require an emulator/device screenshot attempt when available and report exact blocker if unavailable; mark `390x844` desktop as fallback only | fixed |
| LOW | evidence | Screenshot command is specified as target implementation, not currently available in repo | Implement reusable `--ux-review task-card` capture mode during EXEC | follow-up |

- Fixed before continuing: strict first-phone-viewport acceptance, relation editor screenshots, counts scope, Android evidence wording, visual evidence protocol, mobile command model.
- Checks rerun: SPEC linter and rubric tables updated in this file.
- Needs human: confirmation phrase before EXEC.
- Residual risks / follow-ups: Android emulator/device evidence may still be blocked by local workload availability; reusable screenshot command still needs implementation during EXEC.

### Post-EXEC Review
- Статус: PASS для shared desktop/phone layout; Android-runtime validation BLOCKED by local SDK workload.
- Scope reviewed: `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `src/Unlimotion.ViewModel/Resources/Strings*.resx`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`, `tests/Unlimotion.AppAutomation.TestHost/*`, `tests/Unlimotion.ReadmeMedia/Program.cs`, generated UX evidence under `artifacts/ux-review/20260515-1732-task-card/`.
- Decision: можно передавать на human review / PR review. Android evidence is explicitly not considered equivalent to `390x844` desktop fallback until workload and emulator/device validation are available.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Android evidence | Android project build is blocked by local SDK workload `wasm-tools` (`NETSDK1147`) before emulator/device screenshot can be attempted | Run `dotnet workload restore` / install required workloads, then repeat Android build and emulator/device screenshot | blocked by environment |
| LOW | Video evidence | Current repository harness provides still screenshot capture for this UX path, not stable video recording | Use generated screenshot set and `report.json` as fallback evidence; add video only when supported by harness | accepted |
| LOW | Screenshot artifacts | UX screenshots are generated local artifacts and not intended for repository commit | Keep artifacts under ignored `artifacts/ux-review/...`; commit reusable capture command only | done |

- Fixed before final report: compact command row with primary `Новая` and overflow `Еще`, one-column compact layout for `360/390/430`, non-clipping id/meta behavior, denser desktop header/commands, preserved relation editor/tree model, reusable `--ux-review task-card` screenshot command with report.
- Validation evidence:
  - PASS: `dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --no-progress` (`4` tests).
  - PASS: `dotnet run --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*" --no-progress` (`8` tests).
  - PASS: `dotnet build .\src\Unlimotion.Desktop\Unlimotion.Desktop.csproj`.
  - PASS: `dotnet build .\src\Unlimotion.Test\Unlimotion.Test.csproj`.
  - PASS: `dotnet build .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj`.
  - PASS: `dotnet run --project .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review task-card --language ru --no-build-before-launch`; output `artifacts/ux-review/20260515-1732-task-card/`, `Warnings: []`, exact title `Unlimotion README демо`, desktop `1760x1060`, phone `390x844`.
  - PASS: `git diff --check`; only line-ending warnings from Git, no whitespace errors.
  - BLOCKED: `dotnet build .\src\Unlimotion.Android\Unlimotion.Android.csproj` -> `NETSDK1147`, missing workload `wasm-tools`.
- Unrelated changes: не обнаружены; working tree contains only this spec and task-card/screenshot/test related files.
- Needs human: review generated screenshots and decide whether to install Android workloads for device/emulator evidence.
- Residual risks / follow-ups: Android density/keyboard/device-specific behavior still needs real Android screenshot after workload setup; current `390x844` desktop capture remains fallback evidence only.

## Approval
Получено: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction stack and AS-IS review | 0.94 | Нет | Создать follow-up spec | Нет | Нет | Запрос является SPEC-first follow-up after UX screenshot review; central QUEST stack and local UI-test override apply | `AGENTS.md`, `AGENTS.override.md`, central QUEST docs, `specs/2026-05-14-task-card-redesign.md`, `artifacts/task-card-redesign-screenshots/*` |
| SPEC | New spec creation | 0.91 | Нет | Передать пользователю на подтверждение | Да | Да, пользователь попросил оформить отдельную спеку and screenshot instruction | Рекомендации переведены в concrete desktop/mobile design, reusable screenshot protocol and acceptance criteria | `specs/2026-05-15-task-card-mobile-ux-follow-up.md` |
| SPEC | Sanity pass against visual/UI automation rules | 0.93 | Нет | Передать пользователю на подтверждение | Да | Нет | Уточнены exact window title capture and video fallback wording to align with `visual-feedback` and `ui-automation-testing` | `specs/2026-05-15-task-card-mobile-ux-follow-up.md` |
| SPEC | Review fixes and central instruction refresh | 0.94 | Нет | Передать пользователю на повторное ревью/подтверждение | Да | Да, пользователь попросил исправить пункты and перечитать центральный каталог | Исправлены 4 review finding; центральный стек перечитан и применён, но не изменялся, потому что до подтверждения QUEST SPEC разрешает менять только текущую спеку | `C:\Users\Kibnet\.codex\agents\AGENTS.md`, central instruction stack, `specs/2026-05-15-task-card-mobile-ux-follow-up.md` |
| EXEC | Responsive task card implementation | 0.88 | Android runtime evidence | Провести финальную валидацию and update post-EXEC review | Нет | Да, пользователь подтвердил спеку | Реализован shared adaptive layout instead of Android-only duplicate UI; relation model preserved while narrow layout gets compact commands and one-column flow | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.ViewModel/Resources/Strings*.resx` |
| EXEC | UI tests and automation selectors | 0.90 | Android workload availability | Сгенерировать visual evidence | Нет | Нет | Layout assertions expanded to `360/390/430`, selectors updated for changed task-card controls and automation capture scenarios | `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`, `tests/Unlimotion.AppAutomation.TestHost/*` |
| EXEC | UX screenshot protocol implementation | 0.89 | Video recording support in current harness | Завершить post-EXEC review | Нет | Нет | Added reusable real-app FlaUI/Win32 capture mode because headless bitmap evidence was previously nonrepresentative | `tests/Unlimotion.ReadmeMedia/Program.cs`, `artifacts/ux-review/20260515-1732-task-card/*` |
| EXEC | Final validation and review | 0.86 | Android workload `wasm-tools` and emulator/device availability | Передать результат пользователю | Да, для Android workload/device evidence | Нет | Desktop, headless and screenshot checks passed; Android build blocked by SDK workload before app/runtime validation | `specs/2026-05-15-task-card-mobile-ux-follow-up.md`, validation commands |
