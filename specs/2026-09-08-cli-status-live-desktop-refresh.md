# Обновление открытого desktop-клиента после CLI и внешних изменений задач

## 0. Метаданные
- Тип (профиль): `delivery-task`; .NET desktop bugfix с `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing` и локальным требованием Headless/FlaUI coverage.
- Владелец: Kibnet.
- Масштаб: medium, expanded SPEC: затронута общая файловая граница всех mutating CLI commands ↔ desktop и наблюдаемое UI-состояние.
- Целевое семейство / behavior baseline: не применимо — это не модельная задача.
- Поверхность / Effective runtime: Codex desktop, PowerShell/Windows; продуктовая поверхность — Avalonia desktop, файловое хранилище задач и `unlimotion-cli`.
- Eval baseline / evidence: текущая ветка `main` на `23428aa4` (`v1.29.0`); исходники watcher/cache и существующие TUnit/Headless тесты. Отдельный процесс CLI и реальный `FileSystemWatcher` пока не покрыты единым тестом.
- Целевой релиз / ветка: следующая ветка `fix/cli-status-live-refresh`; без commit/push/release в этой задаче, если отдельно не запрошены.
- Ограничения: до approval — изменяется только этот SPEC. В EXEC обязательны TDD, UI-тест и полный тестовый набор; пользовательский task directory не использовать для диагностики.
- Связанные ссылки: `edf83000` / `4f16bb3d` — предыдущее улучшение live storage; `v1.29.0` содержит его как предка, но не является доказательством текущего CLI→UI сценария.

## 1. Overview / Цель
Когда desktop-приложение уже открыто на локальном файловом task space, любое успешное изменение задачи официальным CLI или другим корректным файловым writer должно без ручного обновления отразиться в открытом интерфейсе. В текущем CLI это `set-status`, `complete`, `set-criterion` и `satisfy-criterion`; read-only commands ничего не меняют и поэтому не требуют UI refresh. Создание, удаление и изменение связей пока не предоставляются current CLI, но watcher contract обязан подхватывать их, если внешний writer сохраняет валидные task files.

Outcome contract:

- Success means: после подтверждённой внешней записи desktop UI отражает авторитетное состояние task files — статус, criteria, create/delete и relation graph; список, текущая карточка и status/criteria/tree controls не остаются со старым значением.
- Итоговый артефакт / output: regression suite на реальный CLI→filesystem watcher→desktop UI путь для каждого current mutating command и на generic external writer→create/relation/delete→desktop UI путь; минимальное исправление подтверждённой причины, если current code не проходит сценарий.
- Stop rules: не менять CLI flags/output, формат JSON, правила переходов статусов, task-space configuration, sync/Git workflow или визуальный макет. Если сценарий проходит на исходном коде, а воспроизводится только в установленной старой сборке/другом task directory, остановиться с evidence: не подменять диагностику произвольной переработкой watcher.

## 2. Текущее состояние (AS-IS)
- CLI создаёт самостоятельный `FileTaskStorage` с directory lock и вызывает `TaskGraphCommandService.TrySetStatusAsync` для `set-status`/`complete` и `TrySetCriterionAsync` для `set-criterion`/`satisfy-criterion` (`src/Unlimotion.Cli/Program.cs`). Любой успешный write проходит одну атомарную запись через temporary file и `File.Replace`/`File.Move` (`src/Unlimotion.FileStorage/FileTaskStorage.cs`).
- Desktop создаёт `FileStorage` с `FileDbWatcher`. Watcher регистрирует `FileSystemWatcher` events, throttles UI event на одну секунду, а `FileStorage.OnUpdatingAsync` forced-reloads файл и публикует `Updating`; `UnifiedTaskStorage` переносит обновление на UI thread и hydrates уже существующий `TaskItemViewModel` (`src/Unlimotion.ViewModel/FileDbWatcher.cs`, `src/Unlimotion/FileStorage.cs`, `src/Unlimotion/UnifiedTaskStorage.cs`).
- В `4f16bb3d` добавлены live graph, raw event queue и revision-aware hydration. Это предотвращает ряд гонок локальных операций, но не заменяет executable contract для независимой CLI-записи в запущенный desktop.
- Существующий `FileStorageTaskStatusTests.StatusCommand_AppliesRawWatcherChangeBeforeDebouncedUiUpdate` вручную вызывает fake raw watcher; `HeadlessSessionStorageLifecycleTests` проверяет disposal. Ни один из них не выполняет every mutating CLI command и не проверяет, что уже открытое окно реально сменило status and criteria presentation.

## 3. Проблема
Регрессия «CLI успешно сохранил изменение задачи, но открытый desktop показывает прежние данные» не защищена сквозным тестом для всех mutating commands. Поэтому любой потерянный/неверно интерпретированный native watcher event, атомарная замена файла или неполная доставка в UI cache может пройти существующие unit tests незамеченным.

## 4. Цели дизайна
- Сделать CLI→desktop refresh наблюдаемым и детерминированно проверяемым для каждого current mutating command на временном task space.
- Сделать generic external create, relation update and delete наблюдаемыми и проверяемыми через тот же native watcher contract.
- Сохранять один источник истины — файл после успешной команды CLI; UI только проецирует его.
- Исправлять исключительно подтверждённую границу native file event / `FileStorage` / `UnifiedTaskStorage`.
- Не допускать отката более нового UI/cache state поздним или дублирующим watcher notification.
- Сохранить совместимость существующих файлов, статусов, criteria, history и CLI output.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем polling, ручную кнопку refresh или постоянный background scan как обход без подтверждения необходимости.
- Не меняем публичные CLI команды, их коды выхода/JSON, бизнес-правила `TaskStatusTransitionPolicy` и миграции.
- Не редактируем пользовательские задачи, настройки, Git backup, server/browser/mobile hosts.
- Не добавляем новых public CLI команд для создания задач или редактирования связей: эта SPEC проверяет существующий filesystem-observation contract, а не расширяет CLI surface.
- Не меняем layout, copy, theme или иконографику status picker.
- Не выполняем commit, push, PR, release или установку приложения.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент/файл | Ответственность |
| --- | --- |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenario.cs` | Добавить узкий deterministic `CliLiveRefresh` scenario без связей/repeater. |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenarioData.cs` | Seed `cli-live-refresh` с двумя unsatisfied criteria и unlinked `external-graph-parent` для CLI и graph contracts. |
| `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj` | Добавить reference на CLI assembly, чтобы Headless test мог запускать production CLI как отдельный process. |
| `tests/Unlimotion.UiTests.Headless/...` | Новый UI-regression: открыть `CliLiveRefresh`, выполнить каждый mutating production CLI process над тем же temporary tasks path и дождаться обновлённого UI state после каждой записи. |
| `tests/Unlimotion.UiTests.Headless/...` | Дополнительный external-writer regression: отдельный `FileTaskStorage` с directory lock creates a task, saves a bidirectional relation, then removes it; running desktop must update cache, relation tree and list. |
| `src/Unlimotion.ViewModel/FileDbWatcher.cs` | Изменять только если тест локализует потерю/неверный debounce native rename/change event. |
| `src/Unlimotion/FileStorage.cs` | Изменять только если обновлённый файл не перечитывается/не публикуется с корректным revision. |
| `src/Unlimotion/UnifiedTaskStorage.cs` | Изменять только если опубликованное storage update не гидратирует task cache на UI thread. |
| `src/Unlimotion.Test/FileStorageTaskStatusTests.cs` | Добавить узкий lower-level regression, если найденная причина требует отдельного устойчивого contract test. |

### 6.2 Детальный дизайн
1. Сначала добавить ожидаемо красный Headless UI regression в имеющуюся isolation group `DesktopUi`. Он запускает новый `CliLiveRefresh` scenario на английском, получает созданный AppAutomation temporary `FileStorage.Path` только из запущенного app host и не трогает пользовательские данные. Fixture фиксирован: открытая `cli-live-refresh` initially `Prepared`, has no relations/repeater and contains two unsatisfied criteria `criterion-one` and `criterion-two`; it may legitimately complete after both become satisfied. Тот же isolated scenario seed'ит second unlinked task `external-graph-parent`, чтобы отдельный graph regression мог выбрать его как current item before writing a new child.
2. Каждая команда выполняется отдельным production CLI process по принятому `UnlimotionCliIntegrationTests.RunCli` pattern: `dotnet <typeof(Unlimotion.Cli.Program).Assembly.Location> <command> --tasks <captured path> ... --format json`. Headless test project получает explicit `ProjectReference` на `Unlimotion.Cli`; every process has bounded timeout and captures stdout/stderr. Это подтверждает independent file write, directory lock и native watcher boundary, а не вызов fake watcher/in-process helper.
3. The primary regression runs this exact ordered contract and awaits the UI after every successful write: (a) `set-status --id cli-live-refresh --status InProgress`; (b) `set-criterion --id cli-live-refresh --criterion criterion-one --satisfied true`; (c) `satisfy-criterion --id cli-live-refresh --criterion criterion-two`; (d) `complete --id cli-live-refresh`. A future mutating CLI command must add an equivalent row/test before it is considered covered.
4. После каждого CLI process тест ждёт bounded condition (не фиксированный sleep): persisted task JSON, `MainWindowViewModel.CurrentTaskItem`, matching task-list projection, and relevant rendered control show the expected state. `CurrentTaskStatusButton` (`TaskStatusPicker.Task.Status` and rendered `TaskStatusIcon.Status`) must advance `Prepared → InProgress → Completed`; `CurrentTaskCompletionCriteriaSection` / `CompletionCriteriaItems` must locate each checkbox by `DataContext.Id` and show `criterion-one`, then `criterion-two`, as satisfied.
5. Before `satisfy-criterion criterion-two`, the visible `TaskStatusOptionCompleted` in the opened current-task picker is disabled; after its bounded UI refresh and before the separate `complete` process, that same rendered flyout option is enabled. This proves criteria-derived completion availability, not merely stale checkbox values. Timeout diagnostic includes invoked command, stdout/stderr, temporary directory, persisted status/criteria, VM/control status/criteria and `TaskStatusOptionCompleted.IsEnabled`.
6. Если red test локализует watcher layer, исправить минимальный defect: корректно обработать actual `Changed`/`Created`/`Renamed`/atomic-replace event, дождаться стабильного файла при transient lock и отправить единственное актуальное `Updating` событие. Если defect lies lower/higher, repair stays in the named owner and preserves current raw queue, revision ordering and UI dispatcher boundary.
7. Повторные/delayed events не должны откатывать any task property: authoritative storage revision монотонно принимается, duplicate notification is idempotent, and UI has the same status, criteria and criteria-derived availability as latest valid JSON. Corrupt/zero-length/interrupted file не превращается в ложное обновление и не удаляет последний валидный projection.
8. Visual planning artifact: отдельный wireframe не применим — layout и copy не меняются. Fallback state description: before CLI task and `CurrentTaskStatusButton` show `Prepared`, two unchecked criteria and disabled `TaskStatusOptionCompleted`; after four successful CLI commands the same existing controls show `Completed`, both checked criteria and prior completed-option availability, with no user refresh. Если CLI denied before any write, controls остаются last confirmed; if its result is `OutcomeUnknown`, UI instead converges to the actual valid persisted task snapshot when a write did occur.
9. UI video evidence: отдельное видео не применимо для этого non-layout state bugfix; fallback — inspected Headless/FlaUI UI-tree/screenshot evidence из regression run с before/after status, criteria and completion availability plus test report. Артефакты в `chat-artifacts/...` не коммитятся по умолчанию.
10. A second Headless regression selects seeded `external-graph-parent` and uses a separate `FileTaskStorage` instance with `UseDirectoryLock=true` over the running app's temporary path — not a fake watcher callback — because current CLI has no create/relation commands. It creates `external-graph-child` with no links, then, inside one re-entrant `writer.WithDirectoryLockAsync` scope, saves both final snapshots: parent with `ContainsTasks += external-graph-child` and child with `ParentTasks += external-graph-parent`. For deletion it uses the same lock scope, first persists the final parent snapshot without the child ID and then calls `Remove(external-graph-child)`. This models a valid writer protocol; `Remove` alone is deliberately not claimed to repair reverse links.
11. After child creation, bounded assertions require desktop cache/list admission and persisted child ID. After both relation files have been observed, assertions require the selected parent's VM `ContainsTasks` and the child's VM `ParentTasks` to match their final snapshots, rendered `CurrentItemContainsTree` to contain `external-graph-child`, and, after selecting the child, rendered `CurrentItemParentsTree` to contain `external-graph-parent`. After the ordered unlink/delete, assertions require the persisted parent not to contain the child ID, child file/VM/list item absence, and neither rendered tree to retain the edge. The test never treats a transient half-updated pair as the product result.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| CLI changes current open task | `CliLiveRefresh` displays `cli-live-refresh`; separate CLI processes successfully run all four mutators | Current card/list/status picker/criteria controls reflect each persisted change without reopening app or clicking refresh | Headless UI test plus each persisted CLI result | AC1, AC2 |
| External writer creates task | A separate locked `FileTaskStorage` writes `external-graph-child` into the active path | New child appears in desktop cache/list without reopening app | Headless external-writer test and persisted child file | AC5 |
| External writer changes relation | In one re-entrant lock scope it saves matching final snapshots for `external-graph-parent` and `external-graph-child` | `CurrentItemContainsTree` and `CurrentItemParentsTree` display the new edge, then remove it after ordered unlink/delete | Headless external-writer test and final parent/child persistence assertions | AC5 |
| CLI command is denied before a write | CLI returns validation/business-rule denial and persisted JSON has no new valid version | Desktop keeps last confirmed status; no invented transition | negative automated check | AC3 |
| CLI outcome is uncertain | CLI exits non-zero because `OutcomeUnknown`, while its atomic write may already have changed JSON | Desktop converges to the actual valid persisted state, never to the exit code alone | outcome-unknown automated check | AC3 |
| Native event is duplicated/delayed | Atomic CLI write produces several filesystem notifications | UI ends at latest persisted status, no rollback/crash | focused storage/UI test | AC4 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Desktop VM/control has `Prepared` and two false criteria for `cli-live-refresh` | separate CLI `set-status ... InProgress` succeeds | persisted file, current VM, list projection and picker icon become `InProgress` | wait bounded by test timeout; no manual refresh | first mutable command |
| Task is `InProgress`, criterion-one false | separate CLI `set-criterion ... true` succeeds | persisted JSON and current criteria control show criterion-one true | no status change required | criterion mutation |
| criterion-two false | separate CLI `satisfy-criterion` succeeds | persisted JSON and current criteria control show criterion-two true | no status change required | convenience criterion mutation |
| Task is `InProgress`, only criterion-one true | before criterion-two CLI write | visible `TaskStatusOptionCompleted` is disabled | status picker remains open/reopened as required by current UI pattern | derived presentation baseline |
| Task is `InProgress`, both criteria true | after `satisfy-criterion`, before `complete` | visible `TaskStatusOptionCompleted` becomes enabled | no optimistic direct UI command | derived presentation refresh |
| Task is `InProgress`, both criteria true | separate CLI `complete` succeeds | persisted file, VM/list/picker become `Completed`; criteria remain true | no repeater/cascade in fixture | complete mutation |
| `external-graph-child` absent; selected `external-graph-parent` has no links | separate writer saves valid child | cache/list create child VM | no current CLI create command | generic creation contract |
| Parent/child unlinked | inside one re-entrant directory lock, writer saves both reverse-link final snapshots | parent `ContainsTasks`, child `ParentTasks`, `CurrentItemContainsTree` and `CurrentItemParentsTree` show one edge | assert only after both writes observed | generic relation contract |
| Child exists and linked | in one lock scope writer persists parent unlink, then removes child | final parent JSON/VM lacks child; cache/list and both trees remove child/edge | assert only after parent update and child deletion events | generic deletion contract |
| Desktop VM has any valid status | CLI denied and the task file confirms no new valid state | VM/control unchanged | denial is observable in CLI result and JSON | no optimistic external UI change |
| Desktop VM has any valid task snapshot | any status or criterion CLI write returns `OutcomeUnknown` | VM/control match latest valid persisted JSON, including criteria and derived completion availability | may be a non-zero CLI exit despite a committed write | storage, not exit code, is authoritative |
| Native event pending | duplicate/late event | latest revision wins; no stale rollback | zero-length/corrupt file preserves valid projection | existing live-cache invariant |
| App shuts down | delayed watcher callback | no cache/UI update and no crash | disposal test remains green | existing lifecycle contract |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Scope of symptom | user + agent | Local file-backed desktop task space and every current official mutating command; server mode and read-only CLI commands excluded | 0.99 | Future write commands would need coverage | Нет — user clarified all CLI changes; code lists four current mutators |
| Evidence level | agent | Require real UI host + CLI storage write, not just fake watcher event | 0.98 | Test is slower but protects actual contract | Нет |
| Remedy | agent | TDD diagnosis first; implement only the proven failing layer | 0.97 | No code change if source already passes and issue belongs to a stale installed build | Нет |
| UX | agent | No new refresh control/polling and no visual redesign | 0.94 | A user may prefer immediate feedback affordance | Нет — stated problem requires automatic update |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Task state | task JSON file successfully written by CLI | none; desktop observes all persisted task properties changed by current CLI mutators | no schema/migration | persisted status/criteria and UI state agree after every command |
| CLI | `Program.Main` + `TaskGraphCommandService` | no flags/output change | fully compatible | integration/UI test exit code and output |
| Desktop refresh | native watcher → `FileStorage.Updating` → `UnifiedTaskStorage` | minimal proven correction only | existing projects unaffected | Headless UI scenario + focused test |
| Installed binaries | release packaging | not performed in this scope | N/A | explicit final boundary |

## 7. Бизнес-правила / Алгоритмы
- Every successful current mutating CLI result permits the desktop projection to change. A denied result permits no projection change only after the task file confirms no newer valid persisted state.
- `OutcomeUnknown` is not proof of rollback: any atomic status or criterion write can commit before the CLI reports failure. The desktop watcher must converge to the latest valid persisted task snapshot — status, criteria and derived presentation — even when the CLI process exits non-zero.
- The resulting task snapshot — including status, criteria and derived presentation state — is authoritative from the task file, not the requested command arguments assumed by the UI.
- Watcher delivery is at-least-once: notifications may be repeated or delayed. Applying an older storage revision must not replace a newer projection.
- A valid externally created file hydrates a new VM; a valid external relation change rebuilds the projection; a confirmed external delete removes the VM and its graph edge. For a bidirectional relation writer must persist both final reciprocal snapshots under its re-entrant directory lock; for deletion it must persist parent unlink before removing the child file. These operations are command-agnostic because the watcher observes task-file changes, not CLI command names.
- If a file is temporarily unreadable/corrupt, retain the last valid task projection and do not claim a status transition; a later valid event may recover it.

## 8. Точки интеграции и триггеры
- CLI status commands: `Program.ChangeStatus` invokes `TaskGraphCommandService.TrySetStatusAsync` for `set-status` and `complete` and commits the task file with the directory lock.
- CLI criterion commands: `Program.ChangeCriterion` invokes `TaskGraphCommandService.TrySetCriterionAsync` for `set-criterion` and `satisfy-criterion` and commits the same task file with the directory lock.
- Generic external writer: a separate `FileTaskStorage.Save`/`Remove` atomically changes individual task files under the same directory lock; this is the fallback contract for create/relation/delete until CLI exposes commands for them. A graph writer holds `WithDirectoryLockAsync` across saving both reciprocal final snapshots; deletion persists the parent unlink before `Remove(childId)` because `Remove` does not repair reverse links.
- Filesystem: `FileDbWatcher` receives native events emitted by the atomic file replace.
- Storage: `FileStorage.OnUpdatingAsync` forced-loads stable content and raises `Updating` with the storage revision.
- UI: `UnifiedTaskStorage.TaskStorageOnUpdating` dispatches to Avalonia UI thread, hydrates cache, rebuilds relations; bindings update existing controls.

## 9. Изменения модели данных / состояния
Нет новых persisted fields and no migration. Допустимо лишь скорректировать in-memory watcher queue/revision handling in the existing owner, preserving monotonic revision and task-file compatibility.

## 10. Миграция / Rollout / Rollback
- Migration: не требуется.
- Rollout: обычная следующая desktop/CLI сборка; текущие task directories работают без преобразования.
- Rollback: revert dedicated code/test commit; task files и CLI contract не изменены. No live production operation is authorised.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

1. При запущенном file-backed desktop every successful current mutating CLI command is visible in current UI without a user refresh/restart: `set-status`, `set-criterion`, `satisfy-criterion`, `complete`.
2. Test proves every command uses the same temporary tasks path and a real separate CLI storage write, then checks both persisted value and visible UI/card/list/control state after every command.
3. A denied CLI operation with no new valid persisted task snapshot leaves the desktop at its last confirmed state; an `OutcomeUnknown` status or criterion operation converges the desktop to the actual valid persisted task snapshot if the atomic write occurred.
4. Repeated/delayed watcher notifications do not crash or roll UI back to an older status, criteria value or derived completion availability.
5. A valid externally created `external-graph-child` appears in desktop cache/list; after a writer atomically persists reciprocal final parent/child snapshots under one directory-lock scope, selected parent/child VMs and `CurrentItemContainsTree`/`CurrentItemParentsTree` show the edge. After the writer persists parent unlink then deletes the child, final persistence, cache/list and both trees no longer contain the child/edge — without restart/refresh.
6. Existing status rules, JSON schema, CLI options/output, task space selection and UI layout remain unchanged.
7. Targeted tests, affected build, relevant Headless UI project and full solution test run finish green; any visual fallback evidence is inspected and its exact boundary reported.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | New Headless `DesktopUi` four-command regression | verify status picker, criteria controls and completed-option enabled state after each bounded wait | TUnit result; optional `chat-artifacts/...` screenshot | N/A |
| AC2 | Same test invokes every CLI mutator and reads task file after each | assert command exit/output, captured path equality and snapshot-to-UI parity | test diagnostics/report | N/A |
| AC3 | New/extended denial plus status-and-criterion `OutcomeUnknown` storage/UI tests | compare VM/control and derived availability with persisted JSON, not exit code alone | TUnit result | N/A |
| AC4 | Focused `FileStorageTaskStatusTests` and lifecycle coverage | inspect revision/idempotency assertions | TUnit result | N/A |
| AC5 | New Headless external-writer create/relation/delete regression plus focused storage test | assert final parent/child snapshots, cache/list and `CurrentItemContainsTree`/`CurrentItemParentsTree` only after the paired writes converge | TUnit result, optional screenshot/UI tree | N/A |
| AC6 | Existing CLI/domain/status suites | `git diff --check` and diff review | test reports, diff | N/A |
| AC7 | staged targeted → build → Headless → full TUnit | inspected screenshot/UI tree fallback if produced | reports/paths in final report | not claim green until actually run |

### Planned validation

1. Toolchain/restore preflight and test discovery. Do not re-run an identical timed-out command; inspect progress/lock first.
2. Add four-command and external-writer create/relation/delete characterization regressions and run them before code fix; external graph test uses `external-graph-parent`/`external-graph-child`, reciprocal writes under one re-entrant lock and ordered unlink-before-delete. Expected red is valid only if it fails for a reported stale UI outcome.
3. Run narrow storage/CLI/Headless tests using repository TUnit `--treenode-filter` and `--maximum-parallel-tests 1` for shared Avalonia UI state. The regression itself keeps an explicit current-mutator case table; adding a future CLI write command requires adding a case and is checked in code review because watcher production logic is command-agnostic over every accepted task-file write.
4. `dotnet build src/Unlimotion.sln -p:UseSharedCompilation=false`.
5. Run relevant Headless UI test project; capture fallback visual evidence if rendering is available.
6. Run full solution suite with the repository-proven serial TUnit invocation. Duration/log path are determined by preflight/current test runner; existing historical runs do not stand in for this change.

## 12. Риски и edge cases
- Native `FileSystemWatcher` may coalesce/drop a replace sequence, especially around temporary files; reproduce through production atomic writer, not fake events.
- `FileTaskStorage.Save` is atomic per file, not a graph transaction; a valid external graph writer must keep the directory lock and write reciprocal final snapshots deliberately. The regression asserts eventual final state, not an undocumented no-intermediate-state guarantee.
- A complete task may create related status changes; the deterministic fixture has no repeater/relations, isolating watcher delivery. If the fix touches shared graph logic, targeted tests cover cascades separately.
- Running app may have unsaved editor fields; external status hydration must preserve existing editor ownership guarantees from `4f16bb3d`.
- Test process/project build may hold binaries; use isolated temporary task directory and clean up after test.
- A stale installed executable or a different `--tasks` path is operational evidence, not a reason to weaken source contracts or add polling.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Не хочу кнопку обновления или перезапуск» | This is the reported workflow failure | AC1 requires automatic UI refresh; Non-Goals forbids manual workaround | mitigated |
| «Не ломайте CLI и мои задачи» | Shared file format/automation may be in use | No CLI/schema changes; AC5 runs compatibility coverage | mitigated |
| «Fake watcher test опять ничего не доказывает» | Existing coverage has exactly that gap | AC2 requires actual CLI storage write and a live Headless UI host | mitigated |
| «Не меняйте дизайн ради синхронизации» | Visual change is unnecessary | State-only fallback and no layout/copy changes | mitigated |

### Rework Prevention Checklist
- User-visible behaviour and its no-refresh requirement are named: yes.
- Each observable scenario has evidence: yes, §6.3 and matrix.
- Assumed decisions are listed: yes, Decision Ledger.
- Objections are predicted and mitigated: yes.
- Relevant role review is included below: yes.
- ACs are verifiers, not preparation work: yes.
- EXEC can prove the scenarios before final: yes, through CLI→Headless regression and staged validation.

## 13. План выполнения
1. After exact approval, preflight runner/build and write the isolated end-to-end characterization UI test.
2. Run red test, preserve actual failure diagnostics and localize to native watcher, storage publication or UI hydration.
3. Apply minimal fix only in the proven owner; add focused regression if needed.
4. Run staged validation and capture state evidence.
5. Perform post-EXEC review, report the actual boundary (local source/test evidence vs installed release) without commit/push/release claims.

## 14. Открытые вопросы
Нет блокирующих product decisions. Root-cause layer intentionally remains an EXEC TDD result; the user-visible contract and allowed owners are fixed above. If the exact regression passes on the checked source while a specific installed binary fails, execution stops with version/path evidence for a separate delivery diagnosis.

## 15. Соответствие профилю
- `dotnet-desktop-client`: UI-thread affinity retained; no blocking filesystem work on the UI thread; storage lifecycle/disposal considered.
- `ui-automation-testing`: add/run Headless UI regression, serialise `DesktopUi`, inspect before/after state fallback evidence; no layout artefact is needed because the visual design does not change.
- `testing-dotnet`: TDD red before fix, targeted/affected build/full suite, TUnit `--treenode-filter` rather than VSTest filter.
- Local `AGENTS.override.md`: UI coverage is mandatory and included in AC1/AC6.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenario.cs` | deterministic scenario enum | isolate tests from product fixtures |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenarioData.cs` | current task and two-criteria seed | cover all current CLI mutators |
| `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj` | CLI project reference | resolve production CLI DLL for child-process regression |
| `tests/Unlimotion.UiTests.Headless/Tests/HeadlessSessionStorageLifecycleTests.cs` or dedicated focused Headless test | all-mutator CLI and external-writer create/relation/delete→open desktop UI regressions, denied and uncertain-outcome path | protect user-visible contract |
| `src/Unlimotion.ViewModel/FileDbWatcher.cs` | only if native event capture/debounce is proven faulty | minimal source repair |
| `src/Unlimotion/FileStorage.cs` | only if stable-file reload/publication is proven faulty | minimal source repair |
| `src/Unlimotion/UnifiedTaskStorage.cs` | only if dispatcher/cache hydration is proven faulty | minimal source repair |
| `src/Unlimotion.Test/FileStorageTaskStatusTests.cs` | narrow storage/race regression only if needed | deterministic lower-level proof |
| this SPEC | approval, evidence and review journal | QUEST audit |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Regression coverage | fake watcher and separate CLI tests | every current mutating CLI write → native watcher → running Headless UI test |
| User experience | a reported stale status or criteria may require manual refresh/restart | every successful current CLI change automatically updates existing UI state |
| External graph change | unverified existing watcher expectation | create, relation update and delete update cache/list/tree from valid task files |
| Failure state | not specified by the end-to-end test | denial without a new valid snapshot preserves confirmed UI; uncertain status/criterion outcome converges UI to persisted snapshot |
| Compatibility | current format/CLI contract | unchanged |

## 18. Альтернативы и компромиссы
- Add polling/manual refresh: rejected — masks watcher regression, adds IO and violates automatic-update outcome.
- Revert previous live-cache work: rejected — it protects unrelated revision/editor races and is already released.
- Test only `FileStorage.OnUpdatingAsync` with fake watcher: rejected — misses exact independent CLI atomic-write and UI binding chain.
- Test status only: rejected after user clarification — criteria mutations traverse the same persisted snapshot contract and must be visible too.
- Assume create/relation changes are covered merely because watcher code has `Saved`/`Removed`: rejected — they require an end-to-end external-writer regression.
- Chosen approach: one deterministic isolated task drives every current production write command, verifies each update in the running UI, then permits a minimal localized repair. It gives strong relevant evidence without altering product contracts.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Outcome, AS-IS, gap, scope and Non-Goals are concrete. |
| B. Качество дизайна | 6-10 | PASS | Owners, event flow, idempotency and no-polling boundary stated. |
| C. Безопасность изменений | 11-13 | PASS | No data migration; isolated temporary path and rollback defined. |
| D. Проверяемость | 14-16 | PASS | CLI and external graph scenarios now have executable writer protocol, concrete VM/tree assertions and adversarial re-review. |
| E. Готовность к автономной реализации | 17-19 | PASS | TDD localisation decision and stop rule avoid scope expansion. |
| F. Соответствие профилю | 20 | PASS | Desktop, UI automation and TUnit requirements covered. |

Итог: ГОТОВО; expanded-scope adversarial fallback and re-review below passed.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Observable automatic refresh for all current mutators; explicit excluded read-only commands. |
| 2. Понимание текущего состояния | 5 | CLI mutators, atomic writer, native watcher, storage and UI cache inspected. |
| 3. Конкретность целевого дизайна | 5 | Deterministic four-command executable contract and bounded owner set. |
| 4. Безопасность (миграция, откат) | 5 | No persisted mutation; compatibility and revert scope defined. |
| 5. Тестируемость | 5 | Red regression, negative/race and external-graph tests with final-persistence/UI matrix. |
| 6. Готовность к автономной реализации | 5 | No user-owned decision blocks EXEC. |

Итоговый балл: 30 / 30.

Зона: готово к автономному выполнению после exact approval.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does every current CLI/external task mutation translate to status, criteria and graph presentation without changing domain rules? | PASS | valid writer protocol and final graph state explicit |
| UX / designer | applicable | Does existing UI show the changed state with no unnecessary visual redesign? | PASS | state-only visual fallback explicit |
| Tester / validation | applicable | Does the test cover actual CLI/external writes, status/criteria/availability/graph UI state, denial and duplicate events? | PASS | concrete graph fixture, ordered persistence and VM/tree assertions reviewed |
| Developer / architect | applicable | Are filesystem, cache revision and UI-thread owners bounded and compatible? | PASS | minimal proven-owner rule explicit |
| Delivery / operations / security | applicable | Are production/user data and release side effects excluded? | PASS | temp directory only; no release/push |

### Post-SPEC Review
- Статус / stop decision: `PASS` — expanded external graph scope re-reviewed; implementation remains blocked solely on exact approval.
- Scope/Evidence pass: this SPEC; central QUEST/testing/review/profile owners; `Program.cs`, `FileTaskStorage.cs`, `FileDbWatcher.cs`, `FileStorage.cs`, `UnifiedTaskStorage.cs`; existing CLI, storage and Headless tests; current `git status --short` (only this untracked working SPEC).
- Contract pass: CLI plus generic external filesystem contract verifies status, criteria, creation, deletion and relation updates.
- Adversarial risk / Role-Based pass: prior CLI findings remain fixed. Re-review challenged new-file admission, multi-file relation ordering, relation removal, stale/event races and the incorrect inference that a unit-level `Saved` event proves visible tree update; no actionable residual findings.
- Fix and re-review: complete; adversarial fallback returned `PASS` after reciprocal-write protocol and concrete VM/tree assertions were added.
- Manual-review challenge / остаточные риски: exact native event sequence can differ by filesystem/platform; requested Windows Headless regression is the next-best local evidence, not installed-release proof. The child review was adversarial fallback, not an independently sandboxed read-only review, because effective filesystem access was unrestricted.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | error contract | Non-zero CLI exit can represent `OutcomeUnknown` after a committed write. | Compare UI with valid persisted JSON; test denial and uncertain outcome separately. | fixed and re-reviewed |
| MEDIUM | test-host contract | Draft left CLI process/reference, fixture and UI locator ambiguous. | Fix child-process pattern, `Unlimotion.Cli` reference, `workshop-narrative NotReady → Prepared`, and four UI assertions. | fixed and re-reviewed |
| LOW | evidence | Draft claimed a clean tree although this new SPEC is untracked. | State exact `git status` boundary. | fixed and re-reviewed |
| LOW | review environment | Child reviewer had unrestricted filesystem and cannot be labelled read-only independent. | Record adversarial-fallback status and residual risk truthfully. | accepted residual risk |
| MEDIUM | scope | Previous approved-ready draft covered status only, while user requires every CLI mutation. | Add criteria commands, deterministic fixture and full review. | fixed and re-reviewed |
| MEDIUM | criteria contract | Expanded draft omitted criterion `OutcomeUnknown` and the visible completion-availability refresh. | Compare full persisted snapshot, criteria controls and `TaskStatusOptionCompleted` before/after second criterion. | fixed and re-reviewed |
| MEDIUM | integration map | Criterion CLI commands were absent from §8. | Name `ChangeCriterion` / `TrySetCriterionAsync`. | fixed and re-reviewed |
| LOW | future coverage | A new mutating CLI command would need an explicit new regression case. | Keep test case table as code-review invariant; production watcher remains command-agnostic. | accepted follow-up |
| MEDIUM | generic graph scope | Current review covered status/criteria but not creation, deletion or relations. | Add external-writer E2E scenarios and re-review. | fixed and re-reviewed |
| HIGH | external graph protocol | `Save` is atomic per file and `Remove(child)` does not clear `parent.ContainsTasks`; a UI-only disappearance can hide dangling persisted relation IDs. | Persist both reciprocal snapshots under one re-entrant directory lock; persist parent unlink before child removal; assert final persistence. | fixed and re-reviewed |
| MEDIUM | external graph UI evidence | Draft did not define parent/child IDs, selected item or observable tree controls; `ITaskRelationsIndex` has no public edge query. | Seed `external-graph-parent`, create `external-graph-child`, and assert VM links plus `CurrentItemContainsTree` / `CurrentItemParentsTree`. | fixed and re-reviewed |
| — | итог | Post-SPEC review for external graph scope completed. | Stop at exact approval gate. | PASS |

### Post-EXEC Review
- Статус / stop decision: `PASS` — реализация соответствует утверждённой SPEC; `BLOCKER`/`HIGH`/незакрытых user-owned решений нет.
- Scope/Evidence pass: утверждённая SPEC; `git status --short`; `git diff --stat`; relevant diff для watcher, storage event snapshot, UI cache dispatch, test host и Headless regression; TUnit HTML reports; сборки `Unlimotion.Test` и `Unlimotion.UiTests.Headless`.
- Contract pass: все четыре текущие mutating CLI-команды запускаются отдельными production CLI process над storage открытого desktop; persisted snapshot, task-list/status/criteria controls и derived `Completed` availability сходятся без ручного refresh. Отдельный external writer подтверждает create, reciprocal relation, ordered unlink и delete в cache/list/двух relation trees.
- Adversarial risk pass: проверены duplicate/late per-path events, cross-file relation ordering, alias filename versus `Task.Id`, delete/recreate semantics, corrupt/zero-length guard, disposal race debounce token, stale storage revision и UI synchronization-context boundary.
- Role-Based pass:
  - Business analyst / domain workflow: CLI flags, JSON schema и status/criteria transition rules не менялись; source of truth остаётся persisted task snapshot.
  - UX / designer: layout/copy не менялись; rendered status picker, criteria items, all-tasks projection и relation trees проверены существующими selectors.
  - Tester / validation: expected-red локализовал отсутствие своевременной доставки; добавлены lower-level snapshot assertion и два native-watcher Headless E2E; affected/full tests выполнены serial.
  - Developer / architect: debounce больше не зависит от delayed `MemoryCache` scavenging; storage события сериализованы и несут coherent cloned snapshot/revision; cache mutation возвращена на captured synchronization context.
  - Delivery / operations / security: только временные test task spaces; commit/push/PR/release/install не выполнялись.
- Fix and re-review: после review усилены assertions rendered criteria/list/`TaskStatusOptionCompleted`, добавлена явная persisted snapshot parity, исправлены alias-id lookup и редкая race отмены уже завершившегося debounce token; test-only synchronization-context hook сужен с `public` до `internal`, а snapshot payload перенесён из общего public event args во внутренний subtype `Unlimotion`; сборка и affected tests повторены успешно.
- User-Observable Completion Gate: AC1/AC2 подтверждены `CliChanges_RefreshOpenDesktopProjection`; AC5 — `ExternalWriterCreateRelationAndDelete_RefreshOpenDesktopProjection`; AC3/AC4 — существующими и обновлёнными `UnifiedTaskStorageStatusCommandTests`/`FileStorageTaskStatusTests`; AC6/AC7 — build, diff check, full TUnit и full Headless runs.
- Video fallback: Headless harness не предоставляет безопасный recorder окна; команда проверки — `dotnet test tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj --maximum-parallel-tests 1`; next-best evidence — native control/tree assertions и HTML report `tests/Unlimotion.UiTests.Headless/bin/Debug/net10.0/TestResults/Unlimotion.UiTests.Headless-windows-net10.0-report.html`.
- Validation evidence:
  - `dotnet build tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj --no-restore` — PASS, 0 errors.
  - full Headless serial run — PASS, 40/40; после финального review оба изменённых сценария повторены отдельно — PASS, 1/1 + 1/1.
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj --no-restore` — PASS, 0 errors.
  - full `Unlimotion.Test` serial run — первый проход 952/953 из-за order-dependent `HeadlessUnitTestSession.DisposeAsync` в несвязанном hotkey test; изолированный test PASS 1/1; повторный полный проход PASS 953/953.
  - affected `FileStorageTaskStatusTests` — PASS 22/22; `UnifiedTaskStorageStatusCommandTests` — PASS 14/14 после финальной сборки.
  - solution-level `dotnet test src\Unlimotion.sln` не стартует из-за существующей mixed-runner конфигурации: `global.json` требует Microsoft.Testing.Platform, а `Unlimotion.UiTests.FlaUI` использует VSTest. Это зафиксировано как инфраструктурная граница; TUnit projects выполнены отдельно.
- Depth checklist: scope drift/unrelated changes — нет; acceptance и observable scenarios — сверены; unsupported claims — removed; regression/edge cases — covered; comments/docs/changelog — comments актуальны, changelog не менялся; hidden API/UX/operations contract — нет, snapshot payload и test hook internal; manual-review challenge — наиболее вероятные вопросы про rendered criteria, persisted relations и alias file mapping закрыты тестами/правкой.
- No-findings justification: после исправлений review relevant diff перечитан, `git diff --check` проходит, сборки и affected/full suites дают указанное evidence; остаточный риск ограничен installed desktop/native filesystem variants вне Headless test host.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | tests / UI evidence | Первичная версия нового теста проверяла criteria только через ViewModel и не проверяла `Completed` availability/task-list projection. | Добавить native rendered-control assertions и повторить CLI UI regression. | fixed and re-reviewed |
| MEDIUM | tests / persistence | External graph и CLI assertions недостаточно явно связывали UI с окончательным persisted snapshot. | Читать task files отдельным `FileTaskStorage` и проверять обе стороны relation/unlink. | fixed and re-reviewed |
| MEDIUM | API contract | Headless synchronization-context hook и snapshot property первоначально расширяли публичный API продукта. | Сделать hook `internal`, открыть только test assembly через `InternalsVisibleTo`, а snapshot держать во внутреннем subtype file-storage event. | fixed and re-reviewed |
| LOW | concurrency | `CancellationTokenSource` мог быть disposed одновременно с повторным `ConcurrentDictionary.AddOrUpdate`. | Сделать lifecycle flags volatile и отмену tolerant к completed-dispose race. | fixed and re-reviewed |
| LOW | validation infrastructure | Mixed Microsoft.Testing.Platform/VSTest solution не запускается одной командой. | Выполнить full TUnit/Headless projects отдельно и зафиксировать границу без изменения runner infrastructure в этом scope. | accepted residual risk |
| — | итог | После fix-and-re-review actionable residual findings нет. | Завершить без commit/push/release. | PASS |

## Approval
Получено от пользователя: `Спеку подтверждаю`.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Diagnose reported desktop/CLI stale status | 0.93 | Exact native CLI→UI failure trace in this checkout | Inspect actual writer/watcher/UI path | yes, approval after review | User reported symptom; no approval yet | Existing live-cache code is present but lacks a true end-to-end regression | source/test files listed in §2 |
| SPEC | Design bounded TDD repair | 0.96 | Independent adversarial review | Run reviewer, apply SPEC-only corrections | yes, exact QUEST transition | No | Preserve CLI/data contracts; do not mask with polling | this SPEC |
| SPEC | Adversarial review and rework | 0.98 | Runtime execution evidence remains for EXEC | Wait for exact approval | yes, exact QUEST transition | No | Corrected OutcomeUnknown semantics, concrete child-process fixture and truthful review boundary; no user-owned decision remains | this SPEC, reviewer findings |
| SPEC | Re-review complete | 0.99 | Exact user approval | Stop at SPEC gate | yes, exact QUEST transition | No | Adversarial fallback returned PASS after corrections; only current untracked SPEC changed | this SPEC |
| SPEC | Scope correction from user | 0.99 | Expanded adversarial review | Update SPEC and re-review before renewed approval request | yes, after re-review | User clarified: any CLI changes, not only status | Added all four current mutators and criteria presentation contract; no code changed | this SPEC |
| SPEC | Expanded-scope re-review complete | 0.99 | Exact user approval | Stop at SPEC gate | yes, exact QUEST transition | No | Four mutators, criteria and derived completion availability passed adversarial fallback review; future mutators remain a code-review invariant | this SPEC, reviewer findings |
| SPEC | External graph scope correction | 0.98 | Graph-scope adversarial review | Update SPEC and re-review before renewed approval request | yes, after re-review | User asked about creation and relation changes | Added command-agnostic external create/relation/delete contract; no code changed | this SPEC |
| SPEC | External graph adversarial findings | 0.99 | Re-review of corrected protocol | Specify reciprocal writes and observable trees, then re-review | yes, after re-review | No | `Remove` alone leaves a dangling parent link; generic UI assertions needed concrete fixture/control anchors | this SPEC, reviewer findings |
| SPEC | External graph re-review complete | 0.99 | Exact user approval | Stop at SPEC gate | yes, exact QUEST transition | No | Adversarial fallback returned PASS: reciprocal persistence, ordered unlink/delete and concrete tree assertions are executable | this SPEC, reviewer findings |
| EXEC | Approval received and TDD implementation started | 0.99 | Native failure owner | Add real CLI/external-writer Headless regressions, then repair proven boundary | no | User confirmed exact approval phrase | Scope remained status, criteria, create/delete and relations through shared file observation contract | watcher/storage/cache/test files |
| EXEC | Watcher/storage/cache repair | 0.98 | Full validation and post-EXEC review | Replace delayed debounce, publish coherent snapshot/revision and hydrate cache on captured context | no | No | Real independent CLI process reproduced stale UI; repair is command-agnostic and preserves schema/CLI | `FileDbWatcher.cs`, `FileStorage.cs`, `TaskStorageUpdateEventArgs.cs`, `UnifiedTaskStorage.cs` |
| EXEC | Review findings fixed | 0.99 | Final affected reruns | Strengthen rendered/persisted assertions; harden alias and dispose races | no | No | Manual-review challenge exposed evidence gaps and one cancellation edge case; all had uniquely bounded fixes | Headless/UI selector tests and watcher/storage files |
| EXEC | Validation and completion gate | 0.99 | Installed-release/native non-Headless proof remains outside scope | Record PASS and report result | no | No | Builds, 953-test main suite, 40-test Headless suite and final affected reruns passed; no commit/push/release | this SPEC and local test reports |
