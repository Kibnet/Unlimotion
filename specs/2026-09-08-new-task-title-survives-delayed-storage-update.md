# Защита любых локально изменённых полей от отложенного обновления хранилища

## 0. Метаданные

- Тип (профили): `delivery-task`; `dotnet-desktop-client`, `ui-automation-testing`; контексты `testing-dotnet`, `session-insights-context`.
- Владелец: Codex; решение не требует выбора пользователя.
- Масштаб: medium / expanded SPEC. Короткая форма не подходит: меняется конкуренция UI-редактирования и storage-refresh.
- Целевое семейство / behavior baseline: .NET 10, Avalonia, TUnit; центральные `model-behavior-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `tool-execution-baseline`, `collaboration-baseline`.
- Поверхность: Codex desktop; продуктовая поверхность — локальное Avalonia desktop-приложение.
- Effective runtime: .NET SDK `10.0.400`, Windows `win-x64`; `global.json` задаёт Microsoft.Testing.Platform. Модель/ reasoning не влияют на продуктовый результат.
- Eval baseline / evidence: не применимо — задача не меняет prompt/model behavior. Evidence: regression tests, targeted/headless UI test, targeted FlaUI screenshot; до/после video — fallback ниже.
- Целевой релиз / ветка: текущий detached worktree, публикация/ветка/commit не входят в scope.
- Ограничения: не менять формат данных, интервал автосохранения, публичные API и поведение внешних обновлений после сохранения локальных правок.
- Связанные источники: `src/Unlimotion.ViewModel/TaskItemViewModel.cs`, `src/Unlimotion/FileStorage.cs`, `src/Unlimotion.ViewModel/FileDbWatcher.cs`, `src/Unlimotion/UnifiedTaskStorage.cs`, `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs`.

## 1. Overview / Цель

После любого локального изменения задачи пользователь не должен терять это изменение из-за отложенного события файлового хранилища — независимо от конкретного поля.

Outcome contract:

- Success means: каждое локально изменённое, но ещё не подтверждённое хранилищем поле переживает refresh со старым снимком; после сохранения поле снова принимает последующие authoritative обновления.
- Итоговый артефакт / output: field-level merge-логика `TaskItemViewModel` и регрессионные UI/автоматические проверки для всех групп локально редактируемых полей.
- Stop rules: остановиться и запросить решение, если аудит найдёт поле, которое меняется оптимистично, но не может быть однозначно отнесено ни к локальной pending-редакции, ни к атомарной storage-операции; не выбирать конфликтную политику молча.

## 2. Текущее состояние (AS-IS)

- Новая задача создаётся с пустым `Title`: `TaskTreeManager.AddTask` вызывает `Storage.Save(change)` до того, как пользователь вводит текст.
- `CurrentTaskTitleTextBox` в `MainControl.axaml` привязан к `TaskItemViewModel.Title`.
- Наблюдение свойств сейчас помечает локальную редакцию одной общей revision/snapshot и откладывает `SaveItemCommand` на `TaskItemViewModel.DefaultThrottleTime`, сейчас 10 секунд. Оно уже охватывает title, description, даты/длительность, importance, wanted, repeater и completion criteria, но не хранит, какое именно поле изменено.
- `FileDbWatcher` не подавляет собственные записи по имени файла и выдаёт debounced `OnUpdated` примерно через 1 секунду. Это правильно допускает быстрые внешние изменения, но значит, что сохранение пустой новой задачи может вернуться после начала ввода.
- `UnifiedTaskStorage.HandleTaskStorageUpdatingAsync` загружает snapshot по watcher-событию и вызывает `HydrateCache`; `TaskItemViewModel.Update(taskItem, storageRevision)` безусловно присваивает `Title = taskItem.Title`.
- Уже есть механизм `_latestEditorSnapshot`/revisions для сохранения локальных editable-полей при `SaveItemCommand` и status-операциях, но обычный storage-refresh его не использует. Текущий helper копирует весь набор editor fields и потому не годится как универсальная conflict policy: external изменение не затронутого поля могло бы быть потеряно.

## 3. Проблема

Отложенный storage snapshot считается authoritative для UI раньше, чем локальная revision подтверждена сохранением. Он безусловно заменяет свойства ViewModel. Заголовок новой задачи — наблюдаемый пример, но та же гонка применима к каждому локально изменённому полю; общая snapshot-merge без field ownership также рискованна, потому что может подавить свежие external изменения других полей.

## 4. Цели дизайна

- Сохранить каждое локально изменённое и ещё не persisted поле при refresh из хранилища.
- Применять authoritative поля, не относящиеся к редактору (статус, связи, времена, версия), как и до изменения.
- Ввести один field-level dirty ledger рядом с существующими snapshot/revision helpers, не второй независимый буфер ввода.
- Зафиксировать parameterized regression test для каждой editable field group и реальный пользовательский native UI scenario.
- Не менять debounce watcher-а: он нужен для внешних правок и не является дефектом сам по себе.

## 5. Non-Goals (чего НЕ делаем)

- Не меняем `DefaultThrottleTime` (10 секунд) и не превращаем autosave в сохранение на каждый символ.
- Не отключаем и не подавляем собственные файловые watcher-события по имени файла.
- Не добавляем persisted metadata, блокировки, диалог конфликта или новый публичный API.
- Не объявляем local wins для вычисляемых/storage-owned полей (`Version`, timestamps, availability) и не скрываем external update поля, которое пользователь локально не менял.
- Не выполняем commit, push, PR, release или публикацию артефактов.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `src/Unlimotion.ViewModel/TaskItemViewModel.cs` — хранит field-level dirty ledger для каждого редактируемого persisted field group, применяет только pending local fields поверх принятого storage snapshot и очищает только подтверждённые ревизии этих полей.
- `src/Unlimotion.Test/TaskItemViewModelStorageUpdateTests.cs` — parameterized deterministic tests: stale snapshot для каждой editable group, control for untouched external field, and post-persist authoritative refresh.
- `tests/Unlimotion.UiTests.FlaUI/Tests/NewTaskTitleFlaUiTests.cs` — создаёт задачу через пользовательский flow, вводит заголовок, ждёт watcher window и проверяет UI и persisted значение.

### 6.2 Детальный дизайн

1. Ввести внутренний `[Flags]` ledger `PendingTaskField` и revision per field group: `Title`, `Description`, `Planning`, `Importance`, `Wanted`, `Repeater`, `CompletionCriteria`. Все они соответствуют persisted fields, доступным для локального редактирования через текущую ViewModel.
2. В каждом существующем local mutation source передавать конкретную группу в `MarkEditableChanged`: property-change subscription — по имени свойства; repeater nested changes — `Repeater`; completion-criteria collection/nested changes — `CompletionCriteria`. Аудит EXEC обязан найти все такие sources; новый тест не допускает непокрытую группу.
3. `CapturePendingEditorState` возвращает cloned snapshot вместе с dirty groups, чья field revision новее их подтверждённой revision. При `Update(TaskItem,long)` после storage-revision ordering применять `MergeAuthoritativeStateWithPendingLocalFields(authoritative, editorSnapshot, dirtyGroups)`, который копирует только перечисленные groups.
4. Persisted save acknowledges exactly the groups represented in its captured revision. Поле, изменённое уже после запуска save, остаётся dirty; не затронутое локально поле остаётся authoritative и не копируется из snapshot.
5. Не отмечать storage merge как новую локальную редакцию и не создавать дополнительный save: `Update` уже использует `_isUpdatingFromModel`. Исходное autosave остаётся единственным writer для pending editor data.
6. Status, archive/unarchive и relation commands не меняют persisted property оптимистично до storage result; их existing atomic command-result path остаётся authoritative. EXEC должен зафиксировать audit этого инварианта. Если будет найден optimistic local mutation, он получает field-ledger entry и test до merge.
7. Использовать cloned snapshot, чтобы incoming storage-object и local snapshot не были общими mutable objects.

Visual planning artifact: отдельный wireframe не нужен — layout и copy не меняются. Fallback state storyboard: `пустая новая задача → пользователь печатает «План» → приходит watcher snapshot с пустым Title → в том же TextBox остаётся «План» → autosave сохраняет «План»`.

UI test video evidence: у репозитория есть recorder, но найденный `record-status-contract-evidence.ps1` жёстко привязан к другому status scenario. Для этого нового flow не существует безопасного готового recorder harness. Допустимый fallback: FlaUI создаёт inspected screenshot после того, как title пережил watcher window, плюс подробный test log; в EXEC зафиксировать команду и локальный путь. Если generic recorder уже можно применить без изменения его контракта, он имеет приоритет и будет использован для before/after MP4.

Ошибки: ошибка autosave остаётся в текущем обработчике `SaveItemCommand.ThrownExceptions`; merge не скрывает её и не считает revision persisted.

Производительность: одна clone/merge только при pending local editor revision и одном storage refresh; no polling, no new disk I/O.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Заголовок новой задачи | Создать задачу и начать ввод до watcher refresh | Введённый текст не очищается и остаётся в `CurrentTaskTitleTextBox` | FlaUI test + screenshot/log | AC-1, AC-3 |
| Любое поле редактора | Изменить description, planning, importance, wanted, repeater или completion criterion до refresh | Изменённая group остаётся локальной; incoming snapshot всё ещё обновляет не затронутые groups | parameterized regression test | AC-2, AC-4 |
| Отложенное сохранение | После изменения дождаться autosave | На диске остаётся каждое локально изменённое поле | deterministic regression + FlaUI title persistence check | AC-3 |
| Внешнее обновление без локальной редакции | Storage refresh приходит при отсутствии dirty group | UI принимает authoritative snapshot как раньше | unit regression | AC-5 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Нет dirty groups | Пользователь меняет field group | Только эта group получает local revision и snapshot | Пустой title и cleared date остаются валидными local values | Autosave throttled |
| Одна или несколько dirty groups | Watcher присылает snapshot | Dirty groups остаются local; all other groups обновляются из storage | Повторные refresh не стирают local data | Core regression contract |
| Dirty group изменена после старта save | Первый save подтверждён | Более новая field revision остаётся dirty | Следующий autosave persists newer input | No lost fast typing |
| Dirty groups отсутствуют | Watcher snapshot | Полный authoritative update | External edit любого field отображается | Preserves sync behavior |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Conflict policy during unsaved local edit | agent | Только локально dirty field group побеждает storage snapshot; после save storage again authoritative | 0.96 | External edit того же поля ждёт завершения локального autosave | Нет |
| Scope of fields | agent | Audit and track every current locally editable persisted field group, not a fixed all-editor snapshot | 0.95 | Missing a mutation source would reintroduce loss | Нет |
| Evidence | agent | TUnit + FlaUI screenshot/log; video fallback unless generic harness applies unchanged | 0.90 | Video requirement has explicit objective fallback | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Task editor state | `_latestEditorSnapshot`, global and per-field revisions | Only dirty local groups are merged before `Update` from storage | No persisted shape change | Parameterized unit tests and FlaUI flow |
| File storage refresh | `FileDbWatcher` → `FileStorage` → `UnifiedTaskStorage` | Event continues to refresh cache; only cache conflict resolution changes | No config/migration | Native watcher flow |
| Task JSON | `TaskItem` serialization | No schema/data change | Fully backward-compatible | Persistence assertion |

## 7. Бизнес-правила / Алгоритмы

- Invariant 1: a storage update with revision accepted by ordering may update a ViewModel, but must not discard any field group with a local revision newer than its persisted revision.
- Invariant 2: no non-dirty field is copied from the local snapshot; it retains storage authority even while another group is locally dirty.
- Invariant 3: successful autosave acknowledges only field groups represented by its snapshot revision; a later mutation of the same group remains dirty.
- Invariant 4: every locally editable persisted field is mapped to exactly one dirty group; a new local mutation source must add mapping and a regression case.
- Invariant 5: storage-owned/computed state and command-result-only state retain existing authoritative semantics until an optimistic local mutation is explicitly introduced and tracked.

## 8. Точки интеграции и триггеры

- `TaskTreeManager.AddTask` creates/saves the blank model.
- `FileDbWatcher` delivers debounced `OnUpdated` after creation.
- `FileStorage.OnUpdatingAsync` raises `TaskStorageUpdateEventArgs`.
- `UnifiedTaskStorage.HandleTaskStorageUpdatingAsync` calls `HydrateCache`.
- `TaskItemViewModel.Update(TaskItem,long)` becomes the single merge boundary.

## 9. Изменения модели данных / состояния

Persisted shape не меняется. Вводятся in-memory `PendingTaskField` и per-field revision ledger рядом с существующими `_latestEditorSnapshot`, `_editableRevision`, `_persistedEditableRevision`; они живут только до acknowledgement save или disposal ViewModel.

## 10. Миграция / Rollout / Rollback

- Первый запуск: не требуется.
- Совместимость: существующие task files и settings не меняются.
- Rollback: убрать merge в `TaskItemViewModel.Update(TaskItem,long)` и regression tests; данные на диске не нуждаются в восстановлении.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- AC-1: после создания новой задачи и ввода заголовка до отложенного watcher refresh TextBox продолжает показывать полный введённый текст.
- AC-2: для каждой editable field group stale snapshot не стирает локальное значение и не подавляет incoming value другой, не затронутой group.
- AC-3: после обычного autosave хранение содержит все локально изменённые groups; изменение, сделанное после старта save, не признаётся persisted прежним save.
- AC-4: regression checks детерминированно воспроизводят stale snapshot между local edit и autosave для каждой group и падают на AS-IS.
- AC-5: при отсутствии dirty group storage snapshot всё ещё обновляет каждое authoritative поле как раньше.
- AC-6: targeted unit/UI tests, affected project build и full solution suite завершены green; UI evidence inspected.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1 | New `NewTaskTitleFlaUiTests` creates, types, waits beyond watcher debounce | Inspect screenshot: title is visible and complete | `artifacts/ui-evidence/new-task-title/...png`, local-only | — |
| AC-2 | Parameterized `TaskItemViewModelStorageUpdateTests` covers every ledger group plus an untouched authoritative group | N/A | TUnit test result | — |
| AC-3 | Same class controls a save in flight, then changes same group and verifies next-save ownership | Test output | TUnit log | — |
| AC-4 | Same class sends stale snapshot for every dirty group | N/A | TUnit test result | — |
| AC-5 | Same class applies snapshot with no dirty groups | N/A | TUnit test result | — |
| AC-6 | Targeted TUnit classes → affected project → `dotnet build Unlimotion.sln` → full TUnit solution workflow | Inspect screenshot/video fallback | Commands and runner output | If full suite blocked, report incomplete, not green |

Planned validation commands (run sequentially; exact target filter discovered before execution):

```powershell
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskItemViewModelStorageUpdateTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet run --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -- --treenode-filter "/*/*/*NewTaskTitle*/*" --maximum-parallel-tests 1 --output Detailed
dotnet build src/Unlimotion.sln
dotnet test src/Unlimotion.sln -- --maximum-parallel-tests 1 --output Detailed
```

Stop rules: before long full suite, re-check SDK/restore and report its potentially long duration and output. If an invocation times out, inspect progress/logs and change the hypothesis/scope before retrying; no identical retry. UI failure blocks completion.

## 12. Риски и edge cases

- A newer external edit to the same dirty group arriving while local input is unsaved is intentionally deferred behind the local draft. This is last-local-unpersisted-wins, not a full multi-writer conflict UI.
- Do not use global revision equality as proof that a group is persisted; watcher revision, save revision and field revision are separate domains.
- The mutation-source audit must cover every current persisted editor group. An unregistered future field is a review-blocking defect, not a silent fallback to whole-snapshot merge.
- Creating root/child/sibling tasks all use the same ViewModel update boundary; test design must not bake in root-only behavior.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Не уменьшайте autosave-delay ради маскировки» | Delay protects write load/UX and is unrelated cause | Delay explicitly excluded; merge fixes lost update | mitigated |
| «Не сломайте внешнюю синхронизацию другого поля» | While one field is dirty, another can change externally | Field-level ledger protects only dirty groups; AC-2 and AC-5 | mitigated |
| «Не оставьте новую форму или поле вне защиты» | Existing generic snapshot lacks per-field ownership | Required mutation-source audit, exact group registry and parameterized coverage | mitigated |
| «Это должно быть видно в настоящем окне» | Bug is UI-facing and time-sensitive | FlaUI scenario and inspected screenshot planned | mitigated |
| «Нужна запись до/после» | UI automation profile requires evidence | Specialized existing recorder cannot run this flow; explicit screenshot/log fallback, generic recorder preferred if already compatible | accepted-risk |

### Rework Prevention Checklist

- [x] Named what user sees: every active local edit remains in its field.
- [x] Every scenario maps to evidence.
- [x] Agent decisions and conflict policy are explicit.
- [x] Likely objections have mitigations or an explicit residual risk.
- [x] Role-based review is included below.
- [x] ACs describe observable results, not preparation work.
- [x] EXEC has a test and visual-evidence path.

## 13. План выполнения

1. Audit every source of a local persisted mutation and define the one-to-one `PendingTaskField` registry. Add a parameterized failing test for each group, including a control group updated externally.
2. Implement field-level revisions and merge at the existing `TaskItemViewModel.Update(TaskItem,long)` storage boundary with no new persistence side effect. Recheck status/relation command invariants and add a tracked group if an optimistic path exists.
3. Add/update the FlaUI flow to create a task, type title before watcher debounce expires, wait through the refresh and autosave, then assert UI and persisted value; capture inspected screenshot. The parameterized test is the all-fields proof; FlaUI proves the user-visible watcher flow.
4. Run staged validation in the matrix, inspect evidence, perform post-EXEC review, and report residual video fallback if it remains applicable.

## 14. Открытые вопросы

Нет блокирующих вопросов. Пользователь уточнил policy: защита обязана быть field-level и общей для всех локально изменяемых persisted fields; эта policy однозначно лучше whole-snapshot merge, потому что не перетирает независимое external update.

## 15. Соответствие профилю

- `dotnet-desktop-client`: UI state change will receive UI/integration coverage; no UI-thread blocking is introduced; existing automation IDs remain unchanged.
- `ui-automation-testing`: plan includes stable `CurrentTaskTitleTextBox`, deterministic all-field regression plus FlaUI user flow, screenshot/log fallback for scenario-specific recorder gap, and required targeted UI run.
- local `AGENTS.override.md`: relevant UI test coverage must be added/updated and run before completion.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | Add per-field dirty ledger and merge only pending fields when applying accepted storage update | Prevent stale refresh from erasing any active local edit without hiding other external updates |
| `src/Unlimotion.Test/TaskItemViewModelStorageUpdateTests.cs` | Parameterized stale-snapshot, in-flight save and no-pending controls for every field group | Protect generic algorithmic contract |
| `tests/Unlimotion.UiTests.FlaUI/Tests/NewTaskTitleFlaUiTests.cs` | New real-window create/type/wait/persist flow and screenshot capture | Verify user-visible bug and visual state |
| `specs/2026-09-08-new-task-title-survives-delayed-storage-update.md` | Current work specification and evidence journal | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Any pending field + stale refresh | `Update` overwrites every field from storage snapshot | Only dirty local field groups win until their own save acknowledges them |
| Dirty field A + external update field B | Whole editor snapshot would risk overwriting B | B remains authoritative; only A is preserved locally |
| No pending edits + refresh | Storage snapshot updates every field | Unchanged |
| Autosave | Throttled 10 seconds | Unchanged; acknowledges captured field revisions only |
| Watcher | Debounced event after own/external writes | Unchanged |

## 18. Альтернативы и компромиссы

- Вариант: уменьшить autosave throttle. Минусы: race remains possible, higher write rate, changes unrelated UX/performance policy. Rejected.
- Вариант: suppress own watcher events by filename. Минусы: immediate external edit could be hidden; contradicts current watcher design. Rejected.
- Вариант: copy all editor fields whenever any is dirty. Минусы: protects one input by silently discarding an external change to another input. Rejected.
- Вариант: protect only `Title` or existing fixed helper set. Минусы: leaves analogous fields vulnerable or has the whole-snapshot conflict above. Rejected.
- Chosen: field-level dirty ledger at the storage-application boundary. It is the smallest general solution that preserves any local edit while retaining independent external updates.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, root cause, boundaries and non-goals are concrete. |
| B. Качество дизайна | 6-10 | PASS | Field-level ledger, integration boundary, errors and performance are defined. |
| C. Безопасность изменений | 11-13 | PASS | In-memory state only; no schema migration; rollback is a code revert. |
| D. Проверяемость | 14-16 | PASS | AC-to-test matrix includes negative stale event, normal refresh and UI evidence. |
| E. Готовность к автономной реализации | 17-19 | PASS | Ordered plan, decisions and no blocking open questions. |
| F. Соответствие профилю | 20 | PASS | .NET desktop/UI automation and local override requirements are captured. |

Итог: ГОТОВО.

| № | Блок | Статус | Проверяемое пояснение |
| --- | --- | --- | --- |
| 1 | A | PASS | Цель выражена как сохранение любого видимого локального изменения. |
| 2 | A | PASS | AS-IS содержит создание, binding, autosave, watcher и hydration. |
| 3 | A | PASS | Root cause — stale snapshot до autosave. |
| 4 | A | PASS | Цели дизайна отделяют local editor state и storage authority. |
| 5 | A | PASS | Non-goals запрещают менять throttle/watcher/schema. |
| 6 | B | PASS | Ownership assigned ViewModel, deterministic test and FlaUI test. |
| 7 | B | PASS | Event path and integration triggers listed. |
| 8 | B | PASS | Merge algorithm and three invariants are explicit. |
| 9 | B | PASS | Autosave error path is preserved; no false persistence claim. |
| 10 | B | PASS | Constant-cost clone/merge and no new I/O described. |
| 11 | C | PASS | Existing in-memory revisions/snapshot are named. |
| 12 | C | PASS | No persisted schema/config change; backward compatibility stated. |
| 13 | C | PASS | Code-only rollback without data restoration is defined. |
| 14 | D | PASS | AC-1..AC-6 are observable and measurable. |
| 15 | D | PASS | Matrix maps every AC, including stale and no-pending negative/control cases. |
| 16 | D | PASS | TUnit commands and timeout/stop rules are concrete. |
| 17 | E | PASS | Four ordered implementation/validation stages and dependencies are stated. |
| 18 | E | PASS | Decision ledger has no user-owned blocking choice; open questions are empty. |
| 19 | E | PASS | Expanded form is justified by storage synchronization risk. |
| 20 | F | PASS | Desktop UI automation and local override contracts are mapped. |

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | One visible symptom, general all-field outcome and explicit non-goals. |
| 2. Понимание текущего состояния | 5 | Exact creation, debounce, cache and update chain inspected. |
| 3. Конкретность целевого дизайна | 5 | Single merge boundary, per-field registry and acknowledgement semantics are defined. |
| 4. Безопасность (миграция, откат) | 5 | No data migration; reversible code-only rollback. |
| 5. Тестируемость | 5 | Deterministic, real-window and persistence checks specified. |
| 6. Готовность к автономной реализации | 5 | No user-owned design decision remains. |

Итоговый балл: 30 / 30

Зона: готово к автономному выполнению после exact approval.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does creating a task preserve the user’s immediate input? | PASS | Explicitly cover create, child and sibling through shared boundary. |
| UX / designer | applicable | Does an active field remain stable without layout/copy regression? | PASS | Added state storyboard and FlaUI screenshot requirement. |
| Tester / validation | applicable | Is stale refresh deterministic and is the visible flow covered? | PASS | Added unit negative/control and native UI evidence. |
| Developer / architect | applicable | Does solution preserve cache/storage authority boundaries? | PASS | Reuse existing merge helper, do not alter watcher/throttle. |
| Delivery / operations / security | not applicable | No CI/deploy/config/secrets or external delivery change. | PASS | No change required. |

### Post-SPEC Review

- Статус / stop decision: PASS; можно запрашивать approval.
- Scope/Evidence pass: inspected this amended spec; central instruction stack and local override; task creation/save path; watcher debounce; `UnifiedTaskStorage` cache hydration; every current `TaskItemViewModel` local mutation subscription; existing TUnit/FlaUI test infrastructure; current clean status/diff before this SPEC.
- Contract pass: solution is contained to field-level pending local state vs storage refresh; non-goals protect debounce, data schema and external synchronization; every observable scenario maps to AC/evidence.
- Adversarial risk pass: considered a stale snapshot for each dirty group, external update of a different group, two local edits around a save acknowledgement, no-pending external refresh, repeated watcher events, failed autosave, root/sibling/child creation and false evidence from a headless-only test. Field-level merge preserves only local ownership and returns all other fields to storage authority.
- Role-Based pass: all applicable roles above returned PASS; Delivery is objectively not applicable.
- Findings:

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | scope | User broadened the requirement from title to every local field | Replace whole-editor merge plan with field-level ownership, all-group matrix and mutation-source audit | fixed |
| MEDIUM | specification quality gate | Summary linter table did not individually assess all 20 mandatory criteria | Add criterion-by-criterion table, then rerun spec structure/diff checks | fixed |
| LOW | evidence | Existing MP4 runner is tied to a status scenario, not this flow | Record explicit screenshot/log fallback and prefer generic recorder only if compatible without scope change | accepted-risk |
| LOW | scope | Testing only root task could leave create variants implicit | Fix at shared `TaskItemViewModel.Update` boundary; FlaUI root flow proves triggering race | fixed |

- Fix and re-review: replaced whole-editor preservation with the per-field ledger, added individual linter evidence, shared-boundary rationale and explicit evidence fallback; reran required-section scan and `git diff --check`, then rechecked all-group AC matrix, roles and profiles.
- No-findings justification: after those changes, no BLOCKER/HIGH/MEDIUM finding remains; all mandatory gates have concrete evidence plans.
- Manual-review challenge: a reviewer could demand a proof that no editable field was omitted or that field B stays externally authoritative while A is dirty. The required registry audit and parameterized all-group tests are the direct evidence; native FlaUI additionally proves watcher timing for the user-visible creation flow.
- Residual risks / needs human: only the stated video-harness fallback; it does not block implementation because the profile permits objective fallback.

### Post-EXEC Review

- Статус / stop decision: PASS with documented validation boundaries.
- Implementation pass: replaced the global editor-dirty state with a field-level revision ledger in `TaskItemViewModel`; every local editable mutation is mapped to one of title, description, planning, importance, wanted, repeater, or completion criteria. Storage hydration and the status-command authoritative result preserve only pending groups; all untouched state remains storage-authoritative.
- Test pass: the new parameterized TUnit regression first failed against the old implementation for all seven groups, then passed 9/9 after the fix. It covers field-specific stale refreshes, external authority for every untouched group, a newer local change while an earlier save is in flight, and a no-pending authoritative refresh. New FlaUI coverage drives the actual global-create/title editor flow and waits through the watcher debounce.
- Build/validation boundaries: `Unlimotion.Test` and `Unlimotion.UiTests.FlaUI` target builds passed. The native FlaUI test could not complete on this host because Windows denied FlaUI input injection (`Win32Exception: Access denied`) before the scenario action; this is an objective host-permission limit, not a product assertion. The full solution restore succeeded, but full build stops on pre-existing `WithDeveloperTools` resolution errors in desktop build variants. A serial full `Unlimotion.Test` run was stopped after more than 12 minutes while RavenDB integration tests were still running without a final report; it is not claimed as a pass.
- Findings: no implementation defect found after targeted regression validation. The native desktop result and full-suite result remain the stated residual validation gaps.
- Manual-review challenge: run `NewTaskTitleFlaUiTests` in an interactive Windows CI/desktop session that permits input injection and archive its screenshot/video evidence; then allow the full serial TUnit suite to complete in a dedicated integration runner.

## Approval

Получено: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Diagnose title loss after task creation | 0.96 | Live reproduction not run; code path is deterministic | Prepare expanded spec | Да, EXEC approval | Pending | Creation write + watcher debounce arrives before 10-second autosave and unconditionally sets stale title | `TaskTreeManager.cs`, `FileDbWatcher.cs`, `FileStorage.cs`, `UnifiedTaskStorage.cs`, `TaskItemViewModel.cs` |
| SPEC | Initial design and validation review | 0.94 | Native target-test exact filter will be confirmed in EXEC | Amend scope after user direction | Нет | N/A | Initial whole-editor merge was safe for title but not general enough for independent external field updates | This spec |
| SPEC | All-fields scope amendment and post-SPEC re-review | 0.98 | No blocking design decision; exact mutation-source list will be evidenced in EXEC | Await approval | Да, `Спеку подтверждаю` | Pending | User required all fields; field-level ledger is the smallest design that prevents all local overwrites without discarding other external updates | This spec |
| EXEC | Approval received | 1.00 | None | Add failing regression tests | Нет | User: `Спеку подтверждаю` | Exact required approval was received; implementation may begin within the approved scope | This spec |
| EXEC | TDD and field-level implementation | 0.97 | Interactive native input is unavailable on this host | Run targeted, UI and build validation | Нет | N/A | Seven stale-refresh cases failed before the change; field-level revisions preserve only fields still owned by the local editor | `TaskItemViewModel.cs`, `TaskItemViewModelStorageUpdateTests.cs` |
| EXEC | Validation and residual-risk review | 0.95 | Native FlaUI action and full suite need a capable/long-running runner | Deliver exact boundaries | Нет | N/A | Targeted TUnit passed 9/9; UI target built but its runtime was blocked by host access-denied; full build and full suite failures are reported without attributing them to this change | `NewTaskTitleFlaUiTests.cs`, this spec |
