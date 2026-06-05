# Dense Task Card Redesign

## 0. Метаданные
- Тип (профиль): delivery-task; dotnet-desktop-client + ui-automation-testing
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: сохранить существующие automation-id; не менять публичные команды ViewModel; UI tests обязательны
- Связанные ссылки: запрос пользователя от 2026-06-04

Если секция не применима, указано `Не применимо` с причиной.

## 1. Overview / Цель
Переработать карточку текущей задачи так, чтобы она была плотнее, визуально структурированнее и одинаково работала на широких и узких экранах. Кнопки создания задач нужно вынести из широкой командной панели в меню, открываемое всегда видимой кнопкой `+`.

Outcome contract:
- Success means: карточка сохраняет все основные controls, не даёт горизонтального overflow на 360-430 px, верхняя кнопка `+` видна в компактном и широком режимах и открывает меню создания задач.
- Итоговый артефакт / output: XAML/C# layout changes, обновлённые UI tests, validation evidence.
- Stop rules: остановиться, если layout требует изменения ViewModel API или если релевантные UI tests стабильно не запускаются из-за окружения.

## 2. Текущее состояние (AS-IS)
- Основной layout живёт в `src/Unlimotion/Views/MainControl.axaml`.
- Responsive-перестройка панели задачи живёт в `src/Unlimotion/Views/MainControl.axaml.cs`.
- UI coverage для карточки живёт в `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`.
- Сейчас команды создания отображаются как отдельные кнопки в `CurrentTaskCommandBar`; на узких экранах часть команд прячется в `MoreActions`, но primary create остаётся текстовой кнопкой и занимает горизонтальное место.
- Карточка уже имеет секции header/description/planning/repeater/relations, но отступы и разделители выглядят рыхло для плотной рабочей панели.

## 3. Проблема
Карточка задачи тратит много места на командную панель и рыхлую секционную структуру, а создание задач не собрано в единый постоянный action entrypoint.

## 4. Цели дизайна
- Разделение ответственности: XAML отвечает за визуальную структуру, code-behind только за responsive sizing.
- Повторное использование: использовать существующие команды `Create`, `CreateSibling`, `CreateBlockedSibling`, `CreateInner`.
- Тестируемость: сохранить/добавить automation-id для новой кнопки `+` и пунктов меню.
- Консистентность: оставить текущие Avalonia patterns: `DropDownButton`, `MenuFlyout`, classes, dynamic resources.
- Обратная совместимость: не менять ViewModel API, persistence и task model.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем бизнес-логику создания, удаления, архивирования и связей.
- Не меняем модель данных, storage, localization contract и горячие клавиши.
- Не добавляем новые визуальные библиотеки и не переносим карточку в отдельный control.
- Не меняем flow дерева задач за пределами доступности кнопки `+`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `MainControl.axaml` -> плотная карточка, новая top action row, create menu через `+`.
- `MainControl.axaml.cs` -> responsive widths для header action row/meta row без overflow.
- `MainControlTaskCardLayoutUiTests.cs` -> проверка wide/narrow layout и доступности create menu.

### 6.2 Детальный дизайн
- Visual planning artifact:

```text
Wide details pane
+------------------------------------------------------+
| [x]  [Title editor........................] [+] [⋯]  |
| Wanted | Importance | Archive     id | dates...       |
| Description                                           |
|  [dense multiline editor]                             |
| Planning                                              |
|  [Begin][Set] [Duration][Set] [End][Set]              |
| Repeater                                              |
|  [type] [period] [after complete] [weekdays...]       |
| Relations                                             |
|  Parents [+] tree / Blocking [+] tree / ...           |
+------------------------------------------------------+

Narrow details pane
+------------------------------------+
| [x] [Title editor........] [+] [⋯] |
| Wanted | Importance | Archive      |
| id / dates wrap below              |
| Description                        |
| Planning controls stacked          |
| Repeater controls stacked          |
| Relation editors stay contained    |
+------------------------------------+
```

- `+` menu items: new root task, sibling task, blocked sibling task, inner task.
- Existing `MoreActions` remains for non-create actions such as move/remove and also may keep create commands for compatibility if useful, but creation entrypoint is the `+` button.
- UI test video evidence: fallback. Existing Avalonia.Headless/TUnit suite in this repo provides layout assertions, not safe video recording artifacts.
- Performance: reduce visual tree changes to existing layout area; no new async or blocking work.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Не применимо: change is UI layout and command presentation only.

## 8. Точки интеграции и триггеры
- `Create`, `CreateSibling`, `CreateBlockedSibling`, `CreateInner` stay bound to the same ViewModel commands.
- Layout mode still triggered by `CurrentTaskDetailsScrollViewer.Bounds`.

## 9. Изменения модели данных / состояния
- Не применимо: no model, storage or persisted state changes.

## 10. Миграция / Rollout / Rollback
- Rollout: XAML/code-behind/test changes only.
- Backward compatibility: existing automation IDs for old controls preserved where controls remain; new IDs added for `+` menu.
- Rollback: revert the changed files.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `GlobalTaskCreateMenuButton` is visible and arranged on 1400 px and 360/390/430 px scenarios.
  - Create commands are available through `+` menu.
  - Archive, move and remove commands are available through one task actions menu.
  - Planning preset actions are compact icon dropdowns placed to the right of their value fields.
  - Repeater controls appear to the right of the repeater selector when width permits and wrap safely on phone width.
  - Existing key controls and relation editor remain visible/contained.
  - No horizontal overflow in phone-width task card layout.
  - Wide layout keeps planning/repeater controls compact.
  - Wrapped planning/repeater rows use available width instead of leaving large unused right gaps.
- Tests to add/update:
  - Update `MainControlTaskCardLayoutUiTests` to assert create menu button visibility and classes.
  - Add command/menu assertions without relying on localized text.
  - Add right-edge row assertions for desktop planning/repeater rows and phone weekly weekday toggles.
- Visual acceptance: compare rendered structure through headless layout bounds and containment assertions against the wireframe above.
- UI video evidence: fallback; use headless layout assertions as next-best evidence because current test harness does not emit video.
- Commands for verification:
  - Targeted: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*"`
  - Build: `dotnet build src/Unlimotion.sln`
  - Full tests: `dotnet test src/Unlimotion.sln`
- Stop rules: if full tests fail for unrelated environment/NuGet issues, report blocker with targeted evidence.

## 12. Риски и edge cases
- Flyout contents may be hard to inspect in headless tests; mitigate by asserting stable button/menu structure and command bindings where possible.
- Narrow screens can overflow via fixed widths; mitigate by updating measured widths for header/meta controls.
- Existing tests may assume `CurrentTaskCreateButton`; mitigate by preserving or intentionally updating selectors.

## 13. План выполнения
1. Inspect current XAML and UI tests.
2. Move create commands into always visible `+` dropdown and tighten card/header/section styles.
3. Adjust responsive code-behind sizing if needed.
4. Update UI tests.
5. Run targeted UI tests, then build/full test command if feasible.
6. Run post-EXEC review and report evidence.

## 14. Открытые вопросы
- Нет блокирующих. The user's request defines desired UX enough for a conservative local redesign.

## 15. Соответствие профилю
- Профиль: dotnet-desktop-client; ui-automation-testing
- Выполненные требования профиля: UI tests planned; stable automation-id preserved/extended; no UI-thread blocking; validation commands defined.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Dense card styles, top action row, `+` create menu | Main requested UI change |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Responsive sizing tweaks if needed | Narrow/wide layout support |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Assertions for `+` menu and dense layout | Required UI test coverage |
| `specs/2026-06-04-task-card-dense-redesign.md` | Planning, quality gate and execution journal | QUEST governance |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Create actions | Text buttons in command bar, overflow on compact | Always visible `+` dropdown |
| Header | Completion + title + state/meta rows | Completion + title + primary actions, then compact state/meta |
| Sections | Larger padding/spacing | Denser separators and compact controls |
| Narrow layout | Existing measured widths | Preserve containment with action button available |

## 18. Альтернативы и компромиссы
- Вариант: floating global FAB. Плюсы: always visible. Минусы: larger behavioral change, overlap risk inside desktop split pane.
- Вариант: only keep current overflow menu. Плюсы: minimal. Минусы: does not satisfy `+` request and creation remains less discoverable.
- Выбранное решение: top-row `+` dropdown. Оно keeps scope local, stable and testable while satisfying always-visible create access.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, rollback и perf описаны |
| C. Безопасность изменений | 11-13 | PASS | Данных/миграции нет; rollback очевиден |
| D. Проверяемость | 14-16 | PASS | Acceptance, UI tests и команды проверки заданы |
| E. Готовность к автономной реализации | 17-19 | PASS | План, таблицы и tradeoff описаны |
| F. Соответствие профилю | 20 | PASS | dotnet-desktop-client + ui-automation-testing покрыты |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope and Non-Goals are explicit |
| 2. Понимание текущего состояния | 5 | Current files and contracts identified |
| 3. Конкретность целевого дизайна | 5 | Wireframe and action contract included |
| 4. Безопасность (миграция, откат) | 5 | No data change; rollback is revert |
| 5. Тестируемость | 5 | UI acceptance and commands are defined |
| 6. Готовность к автономной реализации | 5 | No blocking open questions |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-04-task-card-dense-redesign.md`, instruction stack, selected profiles, open questions, planned changed files
- Decision: можно продолжать EXEC по прямому запросу пользователя на реализацию
- Review passes:
  - Scope/Evidence pass: inspected MainControl/task card paths and UI test path.
  - Contract pass: spec matches requested dense card, wide/narrow layout and always-visible `+`.
  - Adversarial risk pass: checked overflow, selector stability and video fallback risk.
  - Re-review after fixes / Fix and re-review: no fixes required.
  - Stop decision: PASS with no blocking open questions.
- Evidence inspected: `MainControl.axaml`, `MainControl.axaml.cs`, `MainControlTaskCardLayoutUiTests.cs`, selected owner documents.
- Depth checklist:
  - Scope drift / unrelated changes: bounded to card layout/tests/spec.
  - Acceptance criteria: explicit and testable.
  - Validation evidence: commands listed.
  - Unsupported claims: none; video fallback marked.
  - Regression / edge case: narrow overflow and selector stability covered.
  - Comments/docs/changelog: no comments/changelog expected.
  - Hidden contract change: ViewModel API remains unchanged.
  - Manual-review challenge: likely concern would be old create button selector; plan preserves stable access via new selector and updates tests.
- No-findings justification: spec is small, localized and has concrete UI/test acceptance.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence fallback is weaker than video artifact | Run relevant headless UI tests and report fallback | accepted-risk |

- Fixed before continuing: none.
- Checks rerun: spec linter/rubric manual pass.
- Needs human: no blocking choice.
- Residual risks / follow-ups: full test suite may be slower or blocked by environment.

### Post-EXEC Review
- Статус: PASS с documented full-suite limitation
- Scope reviewed: approved spec, user visual feedback, follow-up screenshot findings, `git status --short`, `git diff --stat`, relevant XAML/test diff, targeted UI tests, FlaUI suite, desktop/FlaUI builds, UX screenshots, full-suite limitation
- Decision: можно завершать; targeted acceptance and visual evidence passed, full solution test/build is not the gating evidence for this UI iteration
- Review passes:
  - Scope/Evidence pass: reviewed changed files and command outputs listed below.
  - Contract pass: `➕` create menu is global and always visible; `⚙` task actions menu sits under the title after the task identifier and contains move/archive/remove; planning preset buttons sit right of fields; planning stays one row on wide screens; repeater controls are inline when width permits; relation titles/spacing and add controls were reduced.
  - Adversarial risk pass: checked removed old action IDs, UIA/FlaUI visibility, narrow overflow, stale command bindings, repeater wrapping and test harness layout readiness.
  - Re-review after fixes / Fix and re-review: after user feedback, reran card UI tests, create deadline UI tests, FlaUI build/tests and UX capture.
  - Stop decision: PASS for requested scope; solution-level hang/full-suite residual risk is documented.
- Evidence inspected:
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*"` -> PASS, 6/6.
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --maximum-parallel-tests 1` -> PASS, 6/6.
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlDateQuickSelectionUiTests/*"` -> PASS, 1/1.
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlRelationPickerUiTests/*" --maximum-parallel-tests 1` -> PASS, 5/5.
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlNewTaskDeadlineUiTests/*"` -> PASS, 9/9.
  - `dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --no-restore /nr:false` -> PASS, warnings only.
  - `dotnet build src\Unlimotion.sln --no-restore -m:1 /nr:false` -> PASS, warnings only.
  - `dotnet build tests\Unlimotion.UiTests.FlaUI\Unlimotion.UiTests.FlaUI.csproj --no-restore /nr:false` -> PASS, warnings only.
  - `dotnet test tests\Unlimotion.UiTests.FlaUI\Unlimotion.UiTests.FlaUI.csproj --no-build` -> PASS, 9/9.
  - `dotnet run --project tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review task-card --language ru --output-root C:\Users\Kibnet\Pictures\Screenshots\unlimotion-task-card-ux-v5` -> PASS.
  - `git diff --check` -> PASS, line-ending warnings only.
  - `dotnet test src\Unlimotion.sln --no-build -- --maximum-parallel-tests 1` -> FAIL, 469/475 passed; failures include headless task-card arrange when other UI assemblies are started by solution test plus existing tree/outline tests. Affected task-card and quick-date tests pass when isolated in their intended headless process.
  - Earlier full-suite runs had unrelated failures in `SettingsViewModelTests`, `MainControlTreeCommandsUiTests` and outline paste/copy tests; not used as gating evidence for this visual iteration.
  - During the follow-up loop, the first updated card test run failed on repeater wrapping and one transient headless SplitView arrange; repeater layout was fixed and the suite was rerun green.
- Depth checklist:
  - Scope drift / unrelated changes: only task-card XAML, responsive constants, UI selectors/tests and this spec changed.
  - Acceptance criteria: global `➕` menu, polished `⚙` action menu after ID, planning icon actions, one-row wide planning, inline repeater controls, wide/narrow containment and relation editor usability covered by tests/screenshots.
  - Validation evidence: targeted card, quick-date, relation-picker tests, FlaUI suite, desktop/FlaUI/solution builds, diff check and UX capture passed; solution-level test concurrency/full-suite limitation documented.
  - Unsupported claims: no unsupported visual/video claims; screenshot fallback is recorded in `C:\Users\Kibnet\Pictures\Screenshots\unlimotion-task-card-ux-v5`.
  - Regression / edge case: old action button IDs replaced; UIA group overrides kept for FlaUI; test harness waits for arranged details pane.
  - Comments/docs/changelog: no code comments/changelog needed.
  - Hidden contract change: ViewModel commands and model/storage unchanged; automation-id contract intentionally changed from removed text create button to global create menu.
  - Manual-review challenge: likely concern is full test failure; evidence shows affected classes pass in isolation and remaining failures are outside requested surface.
- No-findings justification: requested UI behavior is implemented with localized changes and relevant UI automation coverage passes.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Solution-level test is not current green evidence because solution-level execution starts multiple UI assemblies together and existing tree/outline tests still fail | Report limitation and rely on targeted/UI evidence | accepted-risk |

- Fixed before final report: user feedback iteration for create/action buttons, gear placement and polish, planning presets, one-row wide planning, repeater row, section noise and spacing; UIA/action menu selectors; card layout assertions.
- Checks rerun: card UI tests, quick-date UI test, relation-picker UI tests, create deadline UI tests, desktop/solution builds, FlaUI build/tests, v5 UX capture, diff check.
- Validation evidence: listed above.
- Unrelated changes: none in git status; unrelated failing tests are not modified.
- Needs human: no.
- Residual risks / follow-ups: investigate existing `SettingsViewModelTests` and `MainControlTreeCommandsUiTests` full-suite failures separately.

### Follow-up Review: Wrapped Row Right Edge
- Статус: PASS
- Scope reviewed: user screenshot feedback about unused right gaps after wrapping, `MainControl.axaml.cs`, `MainControlTaskCardLayoutUiTests.cs`, v6 UX screenshots.
- Decision: follow-up complete; update stays inside task-card responsive sizing and UI layout tests.
- Evidence inspected:
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --maximum-parallel-tests 1` -> PASS, 8/8.
  - `dotnet run --project tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review task-card --language ru --output-root C:\tmp\unlimotion-task-card-ux-v6` -> PASS.
  - Visual screenshots:
    - `C:\tmp\unlimotion-task-card-ux-v6\desktop\repeater-planning.png`
    - `C:\tmp\unlimotion-task-card-ux-v6\phone\repeater-planning-card.png`
- Notes: initial sandboxed `dotnet run` attempts failed on NuGet SSL/credentials; the same commands were rerun outside the sandbox and passed.

## Approval
Пользователь запросил реализацию: "Сделай редизайн карточки задачи...".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Define scope and visual plan | 0.86 | Exact rendered sizes before implementation | Edit XAML and UI tests | Нет | Пользователь уже запросил реализацию | Local top-row `+` menu is the smallest change that satisfies creation access and dense layout | `specs/2026-06-04-task-card-dense-redesign.md` |
| EXEC | Implement dense task card and create menu | 0.84 | Final full-suite result | Run targeted UI validation | Нет | Нет | Kept ViewModel commands unchanged and moved create entrypoints to global `+` menu | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs` |
| EXEC | Update UI automation coverage | 0.88 | Full-suite unrelated failures status | Run build and review | Нет | Нет | Updated stable selectors and tests for global create menu, narrow layout and FlaUI UIA visibility | `src/Unlimotion.Test/*`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` |
| EXEC | Validate and review | 0.79 | None for requested scope | Final report | Нет | Нет | Targeted and FlaUI tests pass; full suite has unrelated failures documented | `specs/2026-06-04-task-card-dense-redesign.md` |
| EXEC | Address visual feedback iteration | 0.87 | None for requested scope | Final report | Нет | Пользователь дал 5 конкретных замечаний | Reworked create/action buttons, planning preset icons, repeater row and section density; reran targeted tests and screenshots | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` |
| EXEC | Address screenshot follow-up | 0.90 | None for requested scope | Final report | Нет | Пользователь уточнил 5 screenshot findings | Moved gear under title after ID, resized plus and planning icon dropdowns, forced wide planning to one row, fixed repeater inline controls and captured v3 screenshots | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `specs/2026-06-04-task-card-dense-redesign.md` |
| EXEC | Polish task card to v4 | 0.90 | None for requested scope | Final report | Нет | Пользователь попросил довести до идеала и показать скриншоты | Reduced create button visual weight, unified compact button sizing, tightened planning/repeater widths, added repeater placeholder and captured v4 screenshots | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `specs/2026-06-04-task-card-dense-redesign.md` |
| EXEC | Update quick-date UI coverage | 0.90 | None for requested scope | Final report | Нет | Нет | Updated the localized quick-date test from old text button lookup to stable automation ids and emoji assertions | `src/Unlimotion.Test/MainControlDateQuickSelectionUiTests.cs`, `specs/2026-06-04-task-card-dense-redesign.md` |
| EXEC | Final compact relation polish | 0.91 | None for requested scope | Final report | Нет | Нет | Restyled planning quick actions as visible compact chips, replaced verbose relation add text with compact plus buttons, reran layout/relation/FlaUI checks and refreshed screenshots | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `specs/2026-06-04-task-card-dense-redesign.md` |
| EXEC | Polish gear action button | 0.92 | None for requested scope | Final report | Нет | Пользователь указал, что кнопка с шестеренкой забыта | Enlarged and restyled the task action gear as an accent compact dropdown, stabilized the headless layout helper and captured v5 screenshots | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `specs/2026-06-04-task-card-dense-redesign.md` |
| EXEC | Fill wrapped row right edges | 0.91 | None for requested scope | Commit and update PR | Нет | Пользователь указал на лишние правые отступы при переносе элементов | Recomputed wide planning/repeater widths from available row width, floored weekday toggle widths to avoid layout-rounding wrap, and added desktop/phone right-edge assertions | `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `specs/2026-06-04-task-card-dense-redesign.md` |
