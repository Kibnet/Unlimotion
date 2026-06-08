# Conflict Resolver Mobile Scroll

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`; contexts `testing-dotnet`, `visual-feedback`.
- Владелец: Codex / Unlimotion.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка.
- Ограничения: QUEST mode; до подтверждения менять только эту спецификацию; локальные незакоммиченные изменения пользователя не трогать; UI-facing bugfix требует UI test coverage и попытку Android emulator validation со скриншотами; copy-правка ограничена удалением бесполезной фразы про desktop/mobile из conflict resolver hint.
- Связанные ссылки: `specs/2026-05-10-interactive-sync-conflict-resolution.md`, `specs/2026-05-15-task-card-mobile-ux-follow-up.md`.

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Исправить мобильную вёрстку окна исправления конфликтов синхронизации: при большом количестве конфликтующих файлов пользователь на телефоне должен иметь доступ к списку файлов, содержимому выбранного конфликта и нижним кнопкам действий без ухода важных областей за пределы экрана. Одновременно убрать из подсказки бесполезную для пользователя фразу о том, что окно работает на компьютере и телефоне.

Outcome contract:
- Success means: на телефонной ширине conflict resolver остаётся в пределах viewport; список файлов и детали имеют собственные ограниченные области прокрутки; нижние кнопки доступны без невозможного внешнего скролла; desktop/tablet layout сохраняет двухколоночный сценарий; hint больше не сообщает пользователю, что окно работает на компьютере и телефоне.
- Итоговый артефакт / output: XAML/layout правка `ConflictResolutionControl`, ограниченная resource/copy правка `ConflictResolutionWindowHint`, headless/UI regression test для narrow phone layout с множеством конфликтов, Android emulator screenshot evidence с реально открытым conflict resolver или точный blocker, обновлённый журнал EXEC и review evidence в этой спеки.
- Stop rules: остановиться после утверждения спеки, реализации в её границах, targeted UI test, релевантного build/test, попытки Android build/install/run/screenshot с подтверждённым `ConflictResolutionDialog` в UI tree и post-EXEC review; если Android validation заблокирована окружением или подготовкой conflict-state, зафиксировать точную причину и fallback evidence.

## 2. Текущее состояние (AS-IS)
- Диалог открывается через `DialogHost.Show(settings, "Ask")` в `src/Unlimotion/App.axaml.cs`.
- `src/Unlimotion/Views/MainScreen.axaml` выбирает `views:ConflictResolutionControl` для `SettingsViewModel`.
- `src/Unlimotion/Views/ConflictResolutionControl.axaml`:
  - корневой `Border` имеет `MaxWidth="1080"` и `MaxHeight="760"`;
  - внутри `Grid RowDefinitions="Auto,*"`;
  - `ResolverGrid` в compact mode переводится в один столбец кодом `ConflictResolutionControl.axaml.cs`;
  - список файлов находится в `FileListPane` и `ListBox` имеет `MaxHeight="520"`;
  - детали имеют внутренний `ScrollViewer`, а кнопки действий находятся в нижнем `WrapPanel`.
- `ConflictResolutionControl.axaml.cs` переключает desktop/compact по ширине `< 720`, но не меняет высотные ограничения списка и строк.
- При большом количестве `BackupConflicts` на телефоне список файлов может занять почти всю высоту диалога. Детали выбранного файла и нижние действия оказываются ниже доступного экрана, потому что верхняя область списка не ограничена относительно compact viewport.
- В проекте уже есть `AppAutomation Headless` / `Avalonia.Headless` UI tests и page object `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`.

## 3. Проблема
Одна корневая проблема: compact layout окна conflict resolver ограничивает ширину, но не распределяет высоту между списком файлов, деталями и кнопками действий, поэтому при длинном списке файлов вертикальная мера уходит за размер телефона.

## 4. Цели дизайна
- Разделение ответственности: XAML отвечает за адаптивную структуру и scroll boundaries; code-behind остаётся только за compact/desktop переключение; ViewModel и Git conflict logic не меняются.
- Повторное использование: сохранить существующий shared Avalonia control для desktop и Android.
- Тестируемость: добавить deterministic UI/headless test с маленьким размером окна и большим списком конфликтов.
- Консистентность: не менять команды, automation id и бизнес-семантику разрешения конфликтов; текст менять только для `ConflictResolutionWindowHint`, удаляя фразу про desktop/mobile из RU/EN ресурсов.
- Обратная совместимость: desktop двухколоночный вид должен остаться визуально и функционально прежним.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем алгоритм Git conflict detection/resolution.
- Не добавляем Android-only UI fork.
- Не добавляем production-visible Android поведение только ради теста; допустим только debug/test-only hook, если без него невозможно подготовить conflict-state для emulator evidence.
- Не меняем ViewModel public API без необходимости для тестового сценария.
- Не меняем локализацию/copy вне `ConflictResolutionWindowHint`; разрешённая copy-правка только удаляет фразу про desktop/mobile из RU/EN подсказки окна.
- Не исправляем unrelated dirty files в рабочем дереве.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/ConflictResolutionControl.axaml` -> bounded compact layout: file list, details scroll area and actions remain within dialog viewport.
- `src/Unlimotion/Views/ConflictResolutionControl.axaml.cs` -> if needed, adjust compact row definitions/heights in the existing breakpoint handler.
- `src/Unlimotion.ViewModel/Resources/Strings.resx` and `Strings.ru.resx` -> remove the desktop/mobile phrase from `ConflictResolutionWindowHint`.
- `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` -> extend existing conflict resolver phone-layout coverage with a focused narrow layout regression test.
- `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` -> expose stable conflict resolver controls only if needed for an integration-level Android/headless flow; prefer direct Avalonia visual-tree assertions in the existing test file.
- `specs/2026-06-07-conflict-resolver-mobile-scroll.md` -> execution journal, validation evidence, review results.

### 6.2 Детальный дизайн
- Compact target layout:

```text
phone viewport
+--------------------------------+
| title / hint / status | close  |  Auto
+--------------------------------+
| Conflicting files              |  bounded, scrollable list
| [file 1] [file 2] ...          |  MaxHeight based on compact viewport,
|                                |  not hardcoded 520
+--------------------------------+
| Selected file title            |  details pane consumes remaining height
| scrollable field differences   |  internal ScrollViewer
| ...                            |
+--------------------------------+
| Use current | Use incoming ... |  bottom actions always reachable,
| Finish resolution              |  wrapping allowed
+--------------------------------+
```

- Desktop/tablet target layout remains:

```text
+----------------------+--------------------------------+
| Conflicting files    | Selected file + field details  |
| scrollable list      | scrollable detail body         |
|                      | bottom actions                 |
+----------------------+--------------------------------+
```

- Preferred implementation direction:
  - remove or replace the fixed `ListBox MaxHeight="520"` with layout constraints that let the parent grid allocate space;
  - in compact mode, enforce this invariant: file pane height is bounded to a phone-safe share of the dialog body, details pane keeps a non-zero visible height, and action panel bottom stays within the dialog root bounds;
  - acceptable implementation examples: cap `FileListPane.MaxHeight` from actual dialog/body height, or use star-sized compact rows where file pane cannot consume more than about 35-40% of the resolver body; do not keep an unbounded `Auto,*` compact layout for a long file list;
  - ensure the details pane `Grid RowDefinitions="Auto,*,Auto"` continues to keep action buttons outside the scroll body but inside the pane;
  - use `MinHeight="0"` on star-sized containers if Avalonia measurement requires it for scroll clipping.
- Output contract / evidence rules:
   - UI test must prove bounds, not only selector resolvability: conflict resolver root, file pane, detail pane and bottom action panel must fit within the phone-sized window/dialog bounds.
   - UI test must also prove the file pane is height-bounded with many conflicts and the detail pane has a usable visible height.
   - UI/resource verification must prove `ConflictResolutionWindowHint` no longer contains the RU phrase `работает на компьютере и телефоне` or EN phrase `works on desktop and mobile`.
  - Visual acceptance requires Android emulator screenshots when possible: first dialog viewport, after scrolling file list, and after selecting/inspecting a conflict detail.
  - Android screenshots count only after `uiautomator` confirms that `ConflictResolutionDialog` is present; screenshots of a normal non-conflict app state do not satisfy this spec.
  - If Android build/install/run or conflict-state preparation is blocked, final report must include exact command and blocker, and any desktop narrow screenshot is only fallback evidence.
- UI test video evidence:
  - Existing headless/TUnit runner does not show a known safe video recorder. Fallback: targeted UI test + Android screenshots/logs. If a safe recorder is found during EXEC, capture `after` video locally and record path.
- Boundaries:
   - Do not change command `CanExecute`, `BackupConflicts`, `SelectedBackupConflictFields`, or service methods.
   - Keep existing `AutomationProperties.AutomationId` values stable; add only missing selectors.
- Ошибки: не применимо; bug is layout-only.
- Производительность: fewer rendered visible file rows in a bounded list may improve mobile layout cost; no measured performance target.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Conflict resolution semantics stay unchanged:
  - whole-file actions apply to selected conflict;
  - field-level action applies selected `BackupConflictFieldSelection`;
  - finish is enabled only after all conflicts are resolved.
- The UI must not hide available actions behind unreachable screen space.

## 8. Точки интеграции и триггеры
- `ConflictResolutionControl.OnSizeChanged` remains the trigger for desktop vs compact layout.
- `DialogHost` continues to host the resolver over `MainScreen`.
- UI automation uses stable automation ids:
  - `ConflictResolutionDialog`;
  - `ConflictResolutionFilePane`;
  - `ConflictResolutionFileList`;
  - `ConflictResolutionDetailPanel`;
  - `ConflictResolutionUseCurrentButton`;
  - `ConflictResolutionUseIncomingButton`;
  - `ConflictResolutionApplyFieldsButton`;
  - `ConflictResolutionRefreshButton`;
  - `ConflictResolutionCommitButton`.

## 9. Изменения модели данных / состояния
- Новые persisted fields: не применимо.
- Runtime state: желательно без изменений; headless test setup should populate existing `SettingsViewModel` conflict state through existing methods or controlled test service.
- Android validation state: must be explicitly prepared before screenshots by one of these paths:
  - preferred: seed app-private data with a fixture `Settings.json` and `Tasks` Git repository/index that opens in conflict-resolution mode;
  - fallback only after fixture/capture paths fail and after explicitly recording the blocker: add a debug/test-only launch hook or intent-extra path that creates a `SettingsViewModel` conflict state and opens the existing `ConflictResolutionControl`;
  - either path must be invisible in normal production launch and must not be added just for convenience if existing test/capture routes can prove the fix.
- Storage format: unchanged.

## 10. Миграция / Rollout / Rollback
- Первый запуск: не применимо.
- Обратная совместимость: shared control remains; desktop layout and commands preserved.
- Rollback: revert XAML/code-behind/test changes from this task; no data migration.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. На phone/narrow width conflict resolver switches to one-column compact layout.
  2. With at least 20 conflicting files, file list is scrollable/bounded and does not push details/actions outside the dialog.
  3. Selected conflict details remain visible/inspectable in their own scroll region with a non-zero viewport height.
  4. Bottom action buttons, including `ConflictResolutionUseCurrentButton` or `ConflictResolutionApplyFieldsButton`, have bounds within the phone-sized dialog/window viewport.
   5. Desktop/tablet conflict resolver still uses two columns and preserves existing automation ids.
   6. `ConflictResolutionWindowHint` in RU/EN resources no longer includes the desktop/mobile phrase.
   7. Android emulator validation is attempted with conflict state prepared and screenshots captured only after `ConflictResolutionDialog` is found in UI tree; if blocked, exact blocker is reported.
- Тесты добавить/изменить:
  - Extend existing `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs`, especially `ConflictResolutionControl_UsesSingleColumnLayoutOnPhoneWidth`, or add a sibling test in the same file using a small window such as `390x844` or smaller logical size and a `SettingsViewModel` containing many conflicts.
  - Add missing conflict resolver controls to `MainWindowPage` page object only if an integration-level flow needs them; do not create a new test project/suite solely for this layout bug.
  - If direct bounds are not exposed by AppAutomation wrappers, use Avalonia visual-tree assertions in the headless test for actual bounds, plus page-object resolution for stable selectors.
  - Required bounds assertions: action panel bottom <= dialog root bottom, detail pane bottom <= dialog root bottom, file pane height <= chosen compact cap/share, detail pane actual height > 0 and preferably >= a documented minimum for `390x844`.
- Characterization tests / contract checks:
  - First add a reproducing headless test that fails against current layout if practical.
  - If exact pre-fix assertion is not deterministic because the dialog host measures differently in headless, record that and make the test assert the intended bounded compact contract after the fix.
- Visual acceptance:
  - Android screenshots should show the full dialog header, bounded conflict list, visible selected conflict detail area and reachable bottom buttons.
  - Android UI tree summary must include `ConflictResolutionDialog` before each screenshot set is accepted.
  - No screenshot should show action buttons or detail panel permanently below the phone viewport.
- UI video evidence:
  - Fallback to screenshots because current known UI runner has no confirmed video recorder. If a recorder is found, save `after` video under `artifacts/ui-evidence/conflict-resolver-mobile/`.
- Команды для проверки:

```powershell
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/SettingsControlResponsiveUiTests/*ConflictResolution*"
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release
dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug
adb devices
New-Item -ItemType Directory -Force artifacts/ui-evidence/conflict-resolver-mobile
adb -s <serial> install -r <apk-path>
adb -s <serial> shell cmd package resolve-activity --brief <package>
# Prepare conflict state before launch by seeded app data or debug/test-only hook.
adb -s <serial> shell am start -n <package>/<activity>
adb -s <serial> exec-out uiautomator dump /dev/tty > artifacts/ui-evidence/conflict-resolver-mobile/android-ui.xml
Select-String -Path artifacts/ui-evidence/conflict-resolver-mobile/android-ui.xml -Pattern "ConflictResolutionDialog"
adb -s <serial> exec-out screencap -p > artifacts/ui-evidence/conflict-resolver-mobile/android-after.png
```

- Stop rules для test/retrieval/tool/validation loops:
  - stop targeted test iteration after the new regression test and related build pass;
  - run broader tests unless blocked by time/environment; report exact skipped commands;
  - stop Android loop after successful screenshot evidence or exact environment blocker.

## 12. Риски и edge cases
- Risk: AppAutomation wrappers may not expose visual bounds. Mitigation: use Avalonia visual tree in headless test while keeping stable automation ids in page object.
- Risk: Android build may still be blocked by local workload as in earlier specs. Mitigation: report exact blocker and keep fallback evidence separate.
- Risk: Android app launches without conflict state, producing irrelevant screenshots. Mitigation: require fixture/debug-hook setup and UI tree confirmation of `ConflictResolutionDialog`.
- Risk: debug/test-only Android hook leaks into production behavior. Mitigation: avoid the hook unless fixture/capture paths are blocked; if used, guard it by build configuration or explicit test intent extra and verify normal launch remains unchanged.
- Risk: fixing compact layout could accidentally shrink desktop file list. Mitigation: keep compact changes gated by breakpoint and run existing UI tests.
- Risk: action buttons wrap to multiple rows on Russian text. Mitigation: allow wrapping inside visible action row/panel; acceptance is reachability, not single-row layout.

## 13. План выполнения
1. Extend existing conflict resolver UI tests in `SettingsControlResponsiveUiTests` with a failing/characterization case for many conflicts at phone size.
2. Update `ConflictResolutionControl` layout so compact mode bounds the file list and preserves detail/actions inside viewport.
3. Update `ConflictResolutionWindowHint` in RU/EN resources to remove the desktop/mobile phrase.
4. Add conflict resolver selectors to `MainWindowPage` only if needed for integration evidence.
5. Run targeted UI test and build.
6. Prepare Android conflict state by fixture/capture route first; use debug/test-only hook only if the blocker is recorded and no lower-risk route works.
7. Confirm `ConflictResolutionDialog` in `uiautomator` output and capture screenshots.
8. Run broader relevant .NET tests as feasible.
9. Update EXEC journal and post-EXEC review.

## 14. Открытые вопросы
Нет блокирующих вопросов. Product/UX decision chosen for SPEC: keep the existing one-column compact model and fix vertical bounded scrolling, because the defect is reachability under many files, not conflict resolution semantics or command model.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля:
  - planned UI test coverage for UI-facing bugfix;
  - stable automation ids preserved;
  - no UI-thread blocking operations introduced;
  - planned `dotnet build`, targeted UI tests, broader tests, Android emulator screenshot attempt;
  - video evidence fallback documented with objective current-run limitation.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/ConflictResolutionControl.axaml` | Bounded compact layout for file list/details/actions | Fix phone overflow |
| `src/Unlimotion/Views/ConflictResolutionControl.axaml.cs` | Possibly adjust compact row definitions / sizing | Keep breakpoint behavior consistent |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | Remove desktop/mobile phrase from `ConflictResolutionWindowHint` | Remove useless user-facing copy |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | Remove desktop/mobile phrase from `ConflictResolutionWindowHint` | Remove useless user-facing copy |
| `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` | Extend existing conflict resolver phone layout regression coverage | Catch mobile overflow in established UI test suite |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Add missing conflict resolver selectors only if needed | Stable integration UI automation |
| `src/Unlimotion.Android/MainActivity.cs` or test fixture setup | Conditional debug/test-only conflict-state launch support only after lower-risk fixture/capture routes are blocked | Make Android screenshots prove the target dialog without broad startup changes |
| `specs/2026-06-07-conflict-resolver-mobile-scroll.md` | SPEC/EXEC journal and evidence | QUEST traceability |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Phone conflict list | Long list can consume dialog height | List is bounded and scrolls |
| Phone selected conflict details | Can be pushed below screen | Details remain in remaining viewport and scroll internally |
| Bottom actions | Can become unreachable | Actions stay within visible dialog area / reachable flow |
| Desktop layout | Two columns | Preserved |
| Hint copy | Says the dialog works on desktop and mobile | Only tells the user to resolve sync conflicts before continuing |
| Conflict logic | Existing ViewModel/service commands | Unchanged |

## 18. Альтернативы и компромиссы
- Вариант: make the whole dialog one outer `ScrollViewer`.
  - Плюсы: minimal XAML.
  - Минусы: bottom actions still require long page scroll and details/list compete; does not solve "buttons reachable" well.
  - Почему не выбран: нужен bounded layout with local scroll areas.
- Вариант: collapse file list by default on phone.
  - Плюсы: more detail space.
  - Минусы: changes interaction model and adds product choice.
  - Почему не выбран: existing one-column model can be fixed without hiding core navigation.
- Вариант: Android-only dialog.
  - Плюсы: full phone specialization.
  - Минусы: duplicates shared UI, raises maintenance cost.
  - Почему не выбран: shared Avalonia adaptive layout is enough for this bug.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals описаны. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, layout contract, интеграции, state/rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Scope узкий, API/data migration отсутствуют, rollback понятен. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria проверяют bounds, copy removal, Android conflict-state setup и UI tree evidence. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, альтернативы и открытые вопросы не блокируют EXEC. |
| F. Соответствие профилю | 20 | PASS | UI automation, visual evidence и .NET validation учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Fix targets one mobile overflow bug plus one bounded hint-copy cleanup and excludes conflict logic changes. |
| 2. Понимание текущего состояния | 5 | AS-IS names dialog host, control, compact handler and likely height issue. |
| 3. Конкретность целевого дизайна | 5 | Includes compact wireframe, bounded scroll invariants and action-panel bounds. |
| 4. Безопасность (миграция, откат) | 5 | No data/API migration; rollback is file-level revert. |
| 5. Тестируемость | 5 | Existing conflict resolver UI test extension, copy verification, builds, Android conflict-state screenshots and fallback rules specified. |
| 6. Готовность к автономной реализации | 5 | Steps and risks are bounded with no blocking questions. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-07-conflict-resolver-mobile-scroll.md`; instruction stack `model-behavior-baseline + quest-governance + collaboration-baseline + testing-baseline + testing-dotnet + visual-feedback + dotnet-desktop-client + ui-automation-testing + quest-mode + spec-linter + spec-rubric + review-loops + AGENTS.override.md`; selected profiles; no blocking open questions; planned changed files in section 16.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: inspected current XAML/code-behind/dialog host/test host/page object, Android startup path and prior conflict/mobile specs.
  - Contract pass: SPEC respects QUEST gate, UI automation requirement, visual planning artifact requirement and Android screenshot attempt requirement with target dialog confirmation.
  - Adversarial risk pass: checked hidden risks around desktop regression, unavailable visual bounds, Android workload blockers, Android normal-launch screenshots, leaked debug hooks and button wrapping in Russian.
  - Re-review after fixes / Fix and re-review: fixed review findings by adding Android conflict-state setup, UI tree confirmation, bounds-based test assertions, compact row-sizing invariant, screenshot directory creation, scoped `ConflictResolutionWindowHint` copy cleanup, existing `SettingsControlResponsiveUiTests` route and tighter Android hook fallback.
  - Stop decision: PASS; no BLOCKER/HIGH findings remain before asking for approval.
- Evidence inspected: `ConflictResolutionControl.axaml`, `ConflictResolutionControl.axaml.cs`, `MainScreen.axaml`, `App.axaml.cs`, `MainWindowPage.cs`, `SettingsRemoteTypeHeadlessTests.cs`, `BackupConflictStatus.cs`, `Unlimotion.Android.csproj`, `MainActivity.cs`, prior specs.
- Depth checklist:
  - Scope drift / unrelated changes: dirty working tree exists, but SPEC limits implementation to conflict resolver layout, one hint resource, established UI tests and constrained evidence hooks.
  - Acceptance criteria: includes bounded list, bounds-visible details/actions, desktop preservation, copy removal, Android conflict-state setup and UI tree confirmation.
  - Validation evidence: commands defined; runtime evidence deferred to EXEC by design.
  - Unsupported claims: Android equivalence is not claimed; fallback is explicitly weaker and non-conflict screenshots are rejected.
  - Regression / edge case: desktop layout, long Russian button text, Android state preparation and missing bounds API covered.
  - Comments/docs/changelog: no comments/changelog planned unless code requires it.
  - Hidden contract change: no API/data/command semantics changes planned; optional Android hook must be debug/test-only and only after lower-risk evidence routes are blocked.
  - Manual-review challenge: reviewer should look for star row still measuring unbounded in DialogHost, stale hint copy in either locale, unnecessary new test suite creation, or Android screenshots taken outside conflict mode; EXEC must prove with bounds test, copy check and UI tree/screenshot evidence.
- No-findings justification: SPEC is narrow, includes visual artifact, measurable bounds acceptance, scoped copy cleanup, established UI test target and Android target-state proof; remaining uncertainty is execution/tooling, not design.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | Android evidence | Normal launch screenshots would not prove conflict resolver state | Add conflict-state setup and require `ConflictResolutionDialog` in UI tree before screenshots | fixed |
| HIGH | UI test acceptance | Selector resolvability could pass while controls remain offscreen | Require concrete visual bounds assertions for dialog, file pane, detail pane and action panel | fixed |
| MEDIUM | compact layout design | `Auto,*` row sizing could remain unbounded in compact mode | Add invariant that file pane is capped/star-bounded and details/actions keep visible bounds | fixed |
| MEDIUM | copy scope | Non-Goals previously forbade the requested hint text removal | Allow only `ConflictResolutionWindowHint` RU/EN cleanup and add acceptance/resource verification | fixed |
| MEDIUM | test placement | SPEC previously pointed at a new Headless suite despite existing conflict resolver tests | Extend `SettingsControlResponsiveUiTests` first; add page-object selectors only if integration evidence needs them | fixed |
| MEDIUM | Android scope | Debug/test-only Android launch hook could expand a small layout fix into startup-path work | Make the hook fallback-only after lower-risk fixture/capture routes are blocked and recorded | fixed |
| LOW | screenshot command | Screenshot path directory could be missing | Add `New-Item -ItemType Directory -Force` before screenshot redirection | fixed |
| LOW | evidence | Android emulator validation may still be blocked by local SDK/workload/device availability | Attempt exact commands and report blocker if present | accepted-risk |

- Fixed before continuing: Android conflict-state setup, `uiautomator` confirmation, bounds assertions, compact row-sizing invariant, screenshot directory creation, stable selector list, scoped hint copy cleanup, established test-file route, fallback-only Android hook.
- Checks rerun: SPEC linter/rubric manual pass after fixes.
- Needs human: approval phrase `Спеку подтверждаю`.
- Residual risks / follow-ups: Android runtime evidence may require installing workloads, starting an emulator, or adding a debug/test-only launch hook only if fixture/capture routes are blocked.

### Post-EXEC Review
- Статус: PASS with Android target-dialog fallback
- Scope reviewed: `ConflictResolutionControl.axaml`, `ConflictResolutionControl.axaml.cs`, `Strings.resx`, `Strings.ru.resx`, `SettingsControlResponsiveUiTests.cs`, generated screenshot artifacts and validation command output.
- Decision: можно завершать EXEC; Android APK was built, installed and launched on emulator, but target conflict resolver dialog was not opened because this run has no prepared conflict-state route.
- Review passes:
  - Scope/Evidence pass: inspected actual diff, targeted UI test output, headless UI suite output, desktop and Android build output, after screenshots and emulator screenshots.
  - Contract pass: implementation stayed inside approved files and did not change conflict resolution command/service semantics.
  - Adversarial risk pass: checked stale desktop/mobile copy, unbounded compact file list, action buttons below viewport, desktop two-column regression, post-resolution empty-file-list row reservation and Android non-target screenshots.
  - Re-review after fixes / Fix and re-review: fixed the post-EXEC finding where compact layout still reserved file-list space after all conflicts were resolved; added `ConflictResolutionControl_PhoneWidth_WhenAllConflictsResolved_HidesFilePane`.
  - Stop decision: PASS; target-dialog Android screenshot limitation is recorded and fallback evidence is available.
- Evidence inspected: `artifacts/ui-evidence/conflict-resolver-mobile/after/phone-after.png`, `artifacts/ui-evidence/conflict-resolver-mobile/after/desktop-after.png`, `artifacts/ui-evidence/conflict-resolver-mobile/after/android-emulator-after-wait.png`, `artifacts/ui-evidence/conflict-resolver-mobile/after/android-emulator-menu.png`, `artifacts/ui-evidence/conflict-resolver-mobile/after/android-emulator-settings.png`, Android build/install/launch output.
- Depth checklist:
  - Scope drift / unrelated changes: implementation changed only conflict resolver layout, two resource files, existing UI test file and this spec.
  - Acceptance criteria: bounded list/details/actions proven by `ConflictResolutionControl_PhoneWidth_WithManyConflicts_KeepsDetailsAndActionsVisible`; resolved-state no-file-pane layout proven by `ConflictResolutionControl_PhoneWidth_WhenAllConflictsResolved_HidesFilePane`; copy removal proven by resource text; desktop layout visually preserved in fallback screenshot.
  - Validation evidence: targeted `SettingsControlResponsiveUiTests/*ConflictResolution*` passed 5/5; `Unlimotion.UiTests.Headless` passed 31/31; desktop build passed; Android debug build passed; capture-helper produced nonblank after screenshots; APK launched and Settings was reachable on `emulator-5554`.
  - Unsupported claims: Android target-dialog screenshot is not claimed; emulator screenshots are launch/navigation evidence only because `ConflictResolutionDialog` was not present in UI tree.
  - Regression / edge case: many conflict files, all conflicts resolved with pending commit, wrapped Russian buttons, desktop two-column state and stale copy covered.
  - Comments/docs/changelog: no code comments/changelog needed for scoped UI bugfix.
  - Hidden contract change: no API/data/service command change; no Android debug hook was added.
  - Manual-review challenge: reviewer should inspect phone-after screenshot for visible details/buttons and verify Android evidence is marked fallback, not target-dialog proof.
- No-findings justification: tests and screenshots cover the approved layout/copy contract; Android target-dialog proof is blocked by missing prepared conflict-state data/hook and is reported as fallback.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | Android evidence | Android APK built, installed and launched, but no prepared conflict-state route existed to open `ConflictResolutionDialog` on the emulator without adding a debug hook | Report limitation and use phone-size control screenshot plus UI bounds tests as target-dialog fallback evidence | accepted-risk |

- Fixed before final report: resolved-state compact layout now hides `FileListPane` and uses a single resolver row/column when all conflicts are cleared but commit is pending.
- Checks rerun: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/SettingsControlResponsiveUiTests/*ConflictResolution*"`, `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release`, `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release`, `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug`, `git diff --check`, screenshot capture, Android install/launch/screenshots.
- Validation evidence: targeted UI 5/5 passed; headless UI 31/31 passed; desktop build passed; Android build passed with warnings; `phone-after.png`, `desktop-after.png`, and emulator launch/navigation screenshots generated.
- Unrelated changes: final status includes only files from this task and the untracked spec.
- Needs human: no.
- Residual risks / follow-ups: true Android target-dialog screenshots should be rerun when a seeded conflict-state route or approved debug/test-only hook is available.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создание спеки и quality gate | 0.90 | Подтверждение спеки | Запросить подтверждение пользователя | Да | Да, требуется фраза `Спеку подтверждаю` | Центральный QUEST gate запрещает менять код до утверждения; спецификация фиксирует bounded mobile layout, UI tests and Android screenshot evidence | `specs/2026-06-07-conflict-resolver-mobile-scroll.md` |
| SPEC | Исправление review findings | 0.93 | Подтверждение спеки | Запросить подтверждение пользователя | Да | Да, пользователь попросил исправить review findings | Уточнены Android conflict-state setup, UI tree confirmation, bounds-based acceptance, compact sizing invariant and screenshot directory command; кодовые файлы не менялись | `specs/2026-06-07-conflict-resolver-mobile-scroll.md` |
| SPEC | Исправление повторного review | 0.94 | Подтверждение спеки | Запросить подтверждение пользователя | Да | Да, пользователь попросил исправить findings после review | В scope добавлена ограниченная copy-правка `ConflictResolutionWindowHint`, тестовый маршрут перенесён на существующий `SettingsControlResponsiveUiTests`, Android hook сужен до fallback-only; кодовые файлы не менялись | `specs/2026-06-07-conflict-resolver-mobile-scroll.md` |
| EXEC | Реализация layout/copy/test | 0.82 | Результаты targeted UI test/build/Android evidence | Запустить targeted UI test и build | Нет | Нет | Compact grid переведён на bounded star rows, fixed list max height убран, detail/list containers получают finite measure, RU/EN hint очищен, существующий phone UI-test расширен bounds-проверками на 24 конфликта | `src/Unlimotion/Views/ConflictResolutionControl.axaml`, `src/Unlimotion/Views/ConflictResolutionControl.axaml.cs`, `src/Unlimotion.ViewModel/Resources/Strings.resx`, `src/Unlimotion.ViewModel/Resources/Strings.ru.resx`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs`, `specs/2026-06-07-conflict-resolver-mobile-scroll.md` |
| EXEC | Исправление post-EXEC review finding | 0.88 | Target-dialog Android conflict-state route | Финальные проверки | Нет | Да, пользователь попросил `Исправь` после review | File pane теперь скрывается, а resolver grid становится одной строкой/колонкой, когда все конфликты уже решены и ожидается завершение; добавлен phone-width regression test на этот pending-commit state | `src/Unlimotion/Views/ConflictResolutionControl.axaml.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs`, `specs/2026-06-07-conflict-resolver-mobile-scroll.md` |
| EXEC | Проверки и визуальное evidence | 0.88 | Android target conflict-state route | Завершить отчёт | Нет | Нет | Targeted UI 5/5, desktop build, Android build and headless UI 31/31 passed; phone-size screenshots show bounded list/details/actions; Android APK installed/launched and menu/settings were reachable, but `ConflictResolutionDialog` was not present because no prepared conflict-state route was available | `artifacts/ui-evidence/conflict-resolver-mobile/after/phone-after.png`, `artifacts/ui-evidence/conflict-resolver-mobile/after/desktop-after.png`, `artifacts/ui-evidence/conflict-resolver-mobile/after/android-emulator-settings.png`, `specs/2026-06-07-conflict-resolver-mobile-scroll.md` |
