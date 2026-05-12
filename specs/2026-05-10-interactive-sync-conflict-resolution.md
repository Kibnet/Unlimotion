# Interactive sync conflict resolution

## 0. Метаданные
- Тип (профиль): delivery-task; profiles: dotnet-desktop-client, ui-automation-testing; context: testing-dotnet
- Владелец: Unlimotion desktop / Git backup
- Масштаб: large
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения:
  - Использовать canonical template `C:\Projects\My\Agents\templates\specs\_template.md`.
  - На фазе SPEC менять только этот spec-файл.
  - До фразы `Спеку подтверждаю` не продолжать реализацию и не менять кодовые файлы.
  - Текущие незакоммиченные кодовые изменения по конфликтам считать spike/prototype; в EXEC их нужно сверить со спецификацией, привести к ней или откатить в пределах задачи.
  - Не менять формат task JSON и persisted settings.
  - Не выполнять destructive overwrite локальных задач без явного выбора пользователя.
  - Git-операции и JSON parsing/writing не выполнять на UI-потоке.
  - Для UI-facing изменений добавить/обновить UI tests и запустить релевантный UI suite.
- Связанные ссылки:
  - `AGENTS.md`
  - `AGENTS.override.md`
  - `C:\Projects\My\Agents\AGENTS.md`
  - `C:\Projects\My\Agents\templates\specs\_template.md`
  - `C:\Projects\My\Agents\instructions\governance\routing-matrix.md`
  - `C:\Projects\My\Agents\instructions\core\quest-governance.md`
  - `C:\Projects\My\Agents\instructions\core\quest-mode.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-linter.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-rubric.md`
  - `C:\Projects\My\Agents\instructions\governance\review-loops.md`
  - `C:\Projects\My\Agents\instructions\contexts\testing-dotnet.md`
  - `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`
  - `C:\Projects\My\Agents\instructions\profiles\ui-automation-testing.md`

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
Сделать разрешение Git sync conflicts интерактивным и безопасным: пользователь должен видеть конфликтующие task-файлы, выбирать источник значения для каждого измененного поля (`current` или `incoming`) и, где это детерминированно безопасно, выбирать `merge`.

Outcome contract:
- Success means: при `pull`/`sync` с Git conflict приложение входит в режим разрешения конфликтов, блокирует обычный sync/pull/push/connect, показывает структурированные различия task JSON, применяет выбранные пользователем решения, коммитит и пушит результат только после разрешения всех конфликтов.
- Итоговый артефакт / output: service-level conflict model и resolver, Settings UI flow, локализация, service/ViewModel/UI tests, журнал EXEC-валидации в spec.
- Stop rules: не выпускать feature, если targeted conflict tests или UI test для interactive resolver flow падают; если полный прогон заблокирован окружением, зафиксировать точную команду, ошибку и nearest valid verification.

## 2. Текущее состояние (AS-IS)
- Git backup реализован в `src/Unlimotion/Services/BackupViaGitService.cs`.
- Контракт backup service живёт в `src/Unlimotion.ViewModel/IRemoteBackupService.cs`.
- Settings state и доступность действий живут в `src/Unlimotion.ViewModel/SettingsViewModel.cs`.
- UI настроек живёт в `src/Unlimotion/Views/SettingsControl.axaml`.
- File mode хранит одну задачу в одном JSON-файле без обязательного расширения.
- `FileStorage.Save` пишет JSON через Newtonsoft.Json с `Formatting.Indented`.
- При Git merge conflict LibGit2Sharp предоставляет `ancestor`, `ours/current` и `theirs/incoming` через `repo.Index.Conflicts`.
- До spike у приложения не было полноценного in-app conflict resolver.
- Текущий spike добавляет режим конфликта и whole-file выбор (`UseCurrent` / `UseIncoming`), но не фиксирует трехсторонние field-level правила как контракт.
- Existing test infrastructure:
  - TUnit tests в `src/Unlimotion.Test`.
  - Avalonia.Headless UI tests уже есть в `SettingsControlResponsiveUiTests`.
  - По локальному `AGENTS.md` UI-facing изменения требуют UI tests.
- Проект использует TUnit / Microsoft.Testing.Platform; для targeted runs нельзя использовать VSTest `--filter`, нужно использовать `--treenode-filter`.

## 3. Проблема
Выбор целого файла слишком грубый для задач: локальная версия может менять `Title`, входящая версия - `Description`, и пользователь не должен терять одну из правок. Корневая проблема: нет формального, тестируемого и безопасного контракта field-level conflict resolution для task JSON, поэтому feature нельзя выпускать контролируемо.

## 4. Цели дизайна
- Разделение ответственности:
  - `BackupViaGitService` отвечает за чтение Git conflict entries, построение diff model, применение resolution и Git commit boundaries.
  - `SettingsViewModel` отвечает за UI state, выбранный конфликт, выбранные field decisions и доступность действий.
  - `SettingsControl.axaml` отображает состояние и вызывает команды.
- Повторное использование: whole-file resolver остаётся fallback для unsupported conflicts; field resolver использует тот же conflict lifecycle и finish-flow.
- Тестируемость: каждый supported conflict type имеет service-level test, ViewModel state test и минимум один UI test для пользовательского flow.
- Консистентность: UI conflict mode должен использовать existing Settings patterns, localized strings и stable automation ids.
- Обратная совместимость: формат task JSON и persisted settings не меняются.

## 5. Non-Goals (чего НЕ делаем)
- Не делаем полноценный arbitrary JSON editor.
- Не реализуем ручной line-based text merge с conflict markers.
- Не меняем формат task-файлов.
- Не автоматически выбираем и пушим field-level resolution без явного действия пользователя.
- Не решаем бинарные и non-JSON conflicts по полям.
- Не добавляем GitHub API/OAuth flow.
- Не переписываем file storage или task migration pipeline.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `BackupConflictStatus` / related model in `Unlimotion.ViewModel` -> UI-independent conflict DTOs and user decisions.
- `IRemoteBackupService` -> exposes `GetConflictStatus`, `ResolveConflict`, `ResolveConflictFields`, `CommitResolvedConflicts`.
- `BackupViaGitService` -> maps LibGit2Sharp conflict entries to DTOs; reads ancestor/current/incoming blobs; applies safe JSON token decisions; stages resolved files; commits only after all active conflicts are cleared.
- `SettingsViewModel` -> tracks `IsConflictResolutionMode`, `BackupConflicts`, `SelectedBackupConflict`, field selections and command availability.
- `App.axaml.cs` -> wires async commands, keeps Git operations off UI thread, handles success/error statuses and storage reload after finish.
- `SettingsControl.axaml` -> renders conflict list, field rows, current/incoming/merge choices, whole-file actions and finish action.
- `src/Unlimotion.Test` -> service, ViewModel and Avalonia.Headless coverage.

### 6.2 Детальный дизайн
- Data flow:
  1. `Pull`/`Sync`/`Connect` triggers Git operation.
  2. If Git operation leaves `repo.Index.Conflicts` or conflict operation state, `SettingsViewModel.ReloadGitMetadata()` loads `BackupConflictStatus`.
  3. UI enters conflict mode and disables normal backup actions.
  4. User selects one conflicted file.
  5. If structured JSON conflict is supported, UI shows all fields changed by either side relative to `ancestor`; rows where both sides changed differently are visually marked as real conflicts.
  6. User applies field selections or whole-file resolution for the selected file.
  7. Service writes resolved content inside repository root only, stages only the resolved conflict path and clears that file from index conflicts.
  8. When all conflicts are resolved, `Finish resolution` commits and pushes.
- API contract:
  - `BackupConflictStatus`: `IsInProgress`, `Conflicts`.
  - `BackupConflictFile`: `Path`, `HasCurrentVersion`, `HasIncomingVersion`, `CanResolveByFields`, `Fields`.
  - `BackupConflictField`: immutable service DTO with `FieldPath`, `DisplayName`, current/incoming/merged value previews, `CanMerge`, `DefaultSelection`, `ChangeKind`, `IsRealConflict`.
  - UI/ViewModel selection state is separate from service DTOs; service DTOs must not depend on Avalonia or mutable UI binding state.
  - `BackupConflictFieldSelection`: `FieldPath`, `Source`.
- Output/evidence rules:
  - Resolved file must be valid JSON for structured resolver.
  - JSON parsing for resolver must preserve raw string/date tokens; use `JsonLoadSettings` with `DateParseHandling.None` or an equivalent raw-token preserving approach.
  - Selected JSON tokens should be copied from selected side without type conversion or date reformatting.
  - Structured resolver builds result from `ancestor` and applies every displayed field decision, including one-sided non-conflicting changes.
  - Whole-file fallback must remain available for unsupported conflicts.
  - Service must not push from `ResolveConflict*`; push only happens in finish flow.
  - Whole-file resolution may choose the missing/deleted side; choosing the deleted side removes the file from the working tree and stages deletion.
  - All resolved worktree paths must pass repository-root containment checks; absolute paths and `..` escapes are invalid.
  - `CommitResolvedConflicts` must not stage unrelated modified/untracked files; it may stage only paths that were part of conflict resolution or already staged by resolver.
- Behavior boundaries:
  - No storage format migration.
  - No automatic semantic repair of task relations inside resolver.
  - Existing normal sync flow remains unchanged when no conflict occurs.
- Error handling:
  - Missing repository -> localized `RepositoryNotInitialized`.
  - Missing conflict path -> localized `ConflictNotFound`.
  - Unsupported field resolver input -> localized error and no write.
  - Commit with active conflicts -> localized `ResolveAllSyncConflictsBeforeCommit`.
- Performance:
  - Conflict status reads only conflicted blobs, not the whole repository.
  - Value previews are truncated for UI; full selected tokens remain available in resolver.
  - Long operations run on background tasks.

## 7. Бизнес-правила / Алгоритмы (если есть)
Resolver must use three Git stages: `ancestor`, `current/ours`, `incoming/theirs`. Current-vs-incoming two-way diff is not sufficient for safe defaults.

Supported conflict matrix:
- Structured task JSON:
  - current and incoming entries exist;
  - both parse as JSON objects;
  - field-level resolver is available.
- Delete/modify:
  - only one side exists;
  - field-level resolver is unavailable;
  - whole-file explicit actions are available for both sides;
  - selecting the existing side keeps/restores the file;
  - selecting the missing/deleted side deletes the file and stages deletion.
- Non-JSON or unsupported JSON:
  - parse fails or root is not object;
  - field-level resolver is unavailable;
  - whole-file actions remain available.
- Multiple files:
  - user resolves files one by one;
  - resolved file disappears from active conflict list;
  - finish is enabled only after all active conflicts are gone.

Three-way field rules:
- Show every top-level field that changed in `current` or `incoming` relative to `ancestor`.
- Classify each shown field with `ChangeKind`:
  - `CurrentOnly`: changed only in `current`; default = `current`; not a real conflict.
  - `IncomingOnly`: changed only in `incoming`; default = `incoming`; not a real conflict.
  - `BothSame`: both sides changed to the same value; default = `current`; not a real conflict.
  - `BothDifferent`: both sides changed differently; default = `current`; `IsRealConflict = true` and UI must highlight it separately from non-conflicting changed fields.
- Applying selections always includes all shown fields, not only real conflicts.
- Unknown top-level fields are preserved and participate in same rules.

Merge rules:
- `Title` / `Description` string merge:
  - one empty/null -> non-empty value;
  - equal values -> one value;
  - one full text contains the other -> longer value;
  - otherwise `current + Environment.NewLine + incoming`.
- Relation arrays `ContainsTasks`, `ParentTasks`, `BlocksTasks`, `BlockedByTasks`:
  - merge available;
  - result = union preserving order: current items first, then incoming items not already present;
  - duplicate comparison is ordinal string equality.
- Other scalar arrays:
  - merge available only if all elements are scalar JSON values;
  - result = union preserving current order then incoming unique items.
- Object fields including `Repeater`:
  - v1 behavior: object is shown as one field;
  - merge is unavailable;
  - user chooses the whole object from `current` or `incoming`;
  - nested object rows (`Repeater.Type`, `Repeater.Period`, etc.) are explicitly deferred to a follow-up.
- Scalar fields (`bool`, `number`, `DateTimeOffset`, `TimeSpan`, `null`):
  - merge unavailable;
  - user chooses current/incoming.
- Missing fields:
  - current missing + incoming present -> default incoming;
  - current present + incoming missing -> default current;
  - merge unavailable.

## 8. Точки интеграции и триггеры
- `SyncNowCommand`, `PullCommand`, `CloneCommand` / connect flow:
  - after Git operation or exception, reload Git metadata;
  - if conflict mode detected, do not mark operation as normal error/success.
- `PushCommand`:
  - disabled during conflict mode.
- `SettingsViewModel.ReloadGitMetadata()`:
  - calls `ReloadBackupConflictStatus()`.
- Conflict resolver UI:
  - opens as a modal surface over the main app when conflict mode is detected;
  - uses a shared responsive control, not a Settings-only panel;
  - desktop/tablet width shows file list and field form side-by-side;
  - phone width shows file list above the selected-file form in one column.
- `ResolveConflictUseCurrentCommand` / `ResolveConflictUseIncomingCommand`:
  - apply whole-file resolution for selected conflict.
- `ResolveConflictUseFieldSelectionCommand`:
  - sends selected field decisions to service.
- `CommitConflictResolutionCommand`:
  - commits resolved conflicts and pushes.
- UI automation:
  - stable automation ids for conflict modal, file list, field rows, selected value preview, merge radio and apply/finish buttons.

## 9. Изменения модели данных / состояния
- New runtime DTOs in ViewModel assembly:
  - `BackupConflictStatus`
  - `BackupConflictFile`
  - immutable `BackupConflictField`
  - `BackupConflictFieldSelection`
  - enum for field source / whole-file resolution
  - enum for field `ChangeKind`
- Persisted settings: no new fields.
- Task storage format: unchanged.
- Runtime state:
  - `IsConflictResolutionMode`
  - selected conflict
  - selected field decisions stored in ViewModel/UI state, separate from immutable service DTOs
  - conflict-mode command availability
- Git state:
  - active conflict entries are cleared per resolved file;
  - resolved conflict files/deletions are staged;
  - unrelated modified/untracked files are not staged by conflict resolution commit flow;
  - final resolution commit is created only at finish step.

## 10. Миграция / Rollout / Rollback
- First launch behavior: unchanged.
- Backward compatibility:
  - existing repositories and task files continue to work;
  - unsupported conflicts keep whole-file fallback.
- Rollout:
  1. Ship service resolver behind conflict-mode UI only.
  2. Keep whole-file actions visible as fallback.
  3. Use tests and manual smoke with temporary bare remote before release.
- Rollback:
  - hide/disable field-level resolver (`CanResolveByFields = false`);
  - keep whole-file resolution path;
  - no persisted data rollback required.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. Field-level resolver uses ancestor/current/incoming, not current/incoming only.
  2. Structured task JSON conflicts expose field rows with current/incoming/merge choices.
  3. Structured task JSON conflicts show all changed fields and visually mark real conflicts where both sides changed differently.
  4. Merge choices are available only for fields with deterministic rules.
  5. Applying mixed field selections produces valid JSON and clears the Git conflict for that file.
  6. Delete/modify conflicts can be resolved by selecting the existing side or the missing/deleted side.
  7. Unsupported conflicts fall back to whole-file resolution without crashing.
  8. Normal sync/pull/push/connect actions are disabled during conflict resolution.
  9. Finish resolution commits and pushes only after all conflicts are resolved.
  10. Conflict resolution never writes outside repository root.
  11. Conflict resolution commit flow does not stage unrelated modified/untracked files.
  12. Service tests cover JSON, delete/modify, non-JSON, relation arrays, unknown fields and invalid selections.
  13. ViewModel tests cover action availability and state transitions.
  14. UI tests cover the interactive field selection flow.
  15. Conflict resolution UI is available as a modal surface outside Settings and has a phone-width single-column layout.
- Какие тесты добавить/изменить:
  - `BackupViaGitServiceTests`:
    - `Pull_WithDivergedJsonTask_ExposesAncestorAwareFieldConflicts`
    - `Pull_WithDivergedJsonTask_ShowsAllChangedFieldsAndMarksRealConflicts`
    - `ResolveConflictFields_UsesCurrentIncomingAndMergedValues`
    - `ResolveConflictFields_PreservesUnknownFields`
    - `ResolveConflictFields_CopiesDateTokensWithoutReformattingUsingRawJsonTokens`
    - `ResolveConflictFields_MergesRelationArraysByUnion`
    - `ResolveConflictFields_RejectsMergeForNonMergeableScalar`
    - `ResolveConflictFields_RejectsUnsafeConflictPath`
    - `Pull_WithDeleteModifyConflict_OffersExistingAndDeletedSides`
    - `ResolveConflict_UseDeletedSide_StagesDeletion`
    - `Pull_WithNonJsonConflict_UsesWholeFileOnly`
    - `CommitResolvedConflicts_DoesNotStageUnrelatedFiles`
    - `CommitResolvedConflicts_RejectsWhenAnyConflictRemains`
  - `SettingsViewModelTests`:
    - conflict mode disables normal sync actions;
    - selected conflict exposes field resolver availability;
    - apply field action availability follows selected conflict;
    - finish action enabled only after all conflicts are resolved.
  - `SettingsControlResponsiveUiTests` or focused Settings backup UI test:
    - Settings shows conflict status and an action that opens the resolver;
    - resolver modal appears outside Settings;
    - structured JSON conflict shows field rows and radio choices;
    - real conflict rows are visually distinguishable from non-conflicting changed rows;
    - merge choice updates the selected-value preview;
    - `Apply selected fields` sends expected selections;
    - delete/modify conflict can select the deleted side through whole-file controls;
    - finish button state changes after conflicts are resolved.
    - phone-width resolver uses a single-column layout.
- Characterization tests / contract checks:
  - existing no-conflict sync/pull/push tests must still pass;
  - existing whole-file conflict actions must still pass after field resolver is added.
- Базовые замеры до/после для performance tradeoff: не применимо, feature runs only in conflict mode and reads only conflicted blobs.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/BackupViaGitServiceTests/*"`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/SettingsViewModelTests/*"`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/SettingsControlResponsiveUiTests/*"`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj`
- Stop rules для test/retrieval/tool/validation loops:
  - targeted service/ViewModel/UI failures block completion;
  - full-run failures must be diagnosed as regression vs unrelated existing issue;
  - unsupported `--filter` runner errors must be corrected to `--treenode-filter`, not treated as test evidence.

## 12. Риски и edge cases
- Risk: two-way merge loses independent changes.
  - Mitigation: require ancestor-aware three-way field classification.
- Risk: relation array union resurrects deleted relations.
  - Mitigation: merge preview, explicit user choice, service tests for add/delete combinations.
- Risk: unknown JSON fields are dropped.
  - Mitigation: operate on `JObject`/tokens, not `TaskItem` serialization.
- Risk: DateTime/TimeSpan formatting changes.
  - Mitigation: copy selected JSON tokens exactly.
- Risk: malformed/non-task files crash resolver.
  - Mitigation: fallback whole-file flow.
- Risk: UI becomes too dense in Settings.
  - Mitigation: show field panel only for selected file; wrap/truncate previews; cover narrow viewport.
- Risk: Git index state becomes inconsistent after applying resolution.
  - Mitigation: service tests assert conflict entry cleared, commit succeeds and remote content matches expected result.
- Risk: push fails after local resolution commit.
  - Mitigation: no rollback; clear error; user can retry push.
- Risk: current spike diverges from spec.
  - Mitigation: Phase 0 requires spike alignment before further coding.
- Risk: conflict resolver accidentally commits user edits unrelated to the Git conflict.
  - Mitigation: stage only resolved conflict paths; add regression test with unrelated modified/untracked files.
- Risk: unsafe conflict path writes outside repository root.
  - Mitigation: require relative path and repository-root containment check before any write/delete.

## 13. План выполнения
1. Phase 0 - Align spike with spec:
   - compare existing uncommitted code against this spec;
   - remove or adjust prototype pieces that rely on two-way diff only;
   - do not continue product implementation before user approval.
2. Phase 1 - Service model and resolver:
   - add ancestor-aware conflict DTOs;
   - implement whole-file and field-level resolver;
   - add service tests for supported matrix.
3. Phase 2 - ViewModel and UI:
   - add state, commands and availability rules;
   - add field resolver UI with stable automation ids;
   - add localization.
4. Phase 3 - UI automation and integration:
   - add/extend Avalonia.Headless tests;
   - run targeted service/ViewModel/UI tests.
5. Phase 4 - Validation and review:
   - run build and full available tests;
   - run post-EXEC review;
   - update spec journal with validation evidence.

## 14. Открытые вопросы
Не применимо. Пользовательские решения от 2026-05-10 зафиксированы:
- показываем все поля, измененные любой стороной относительно `ancestor`, а реальные конфликты выделяем отдельно через `IsRealConflict`;
- для delete/modify conflict можно выбрать удаленную/отсутствующую сторону, и это означает staged deletion.
- object fields, включая `Repeater`, в v1 показываются как одно поле с выбором whole object `current` / `incoming`; nested object rows and object merge deferred to follow-up.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`; context `testing-dotnet`.
- Выполненные требования профиля:
  - Длительные Git/JSON операции вынесены из UI-потока в design contract.
  - UI-facing flow требует UI tests.
  - Stable automation ids закреплены как часть acceptance criteria.
  - TUnit/Microsoft.Testing.Platform runner syntax указан через `--treenode-filter`.
  - Build + targeted + full tests указаны как validation path.
  - Platform-specific code remains in service layer; ViewModel stays UI state oriented.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-05-10-interactive-sync-conflict-resolution.md` | Рабочая спецификация и журнал | Central QUEST contract |
| `src/Unlimotion.ViewModel/BackupConflictStatus.cs` | Runtime conflict DTOs/enums | UI-independent conflict model |
| `src/Unlimotion.ViewModel/IRemoteBackupService.cs` | New resolver service methods | Service/ViewModel boundary |
| `src/Unlimotion/Services/BackupViaGitService.cs` | Ancestor-aware conflict status, field resolver, commit guard | Core Git conflict behavior |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | Conflict mode state, selected conflict, action availability | Settings state and bindings |
| `src/Unlimotion/App.axaml.cs` | Async command wiring and conflict flow integration | UI command execution and storage reload |
| `src/Unlimotion/Views/SettingsControl.axaml` | Conflict panel, field rows, action buttons, automation ids | User-facing resolver UI |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | EN strings | Localization |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | RU strings | Localization |
| `src/Unlimotion.Test/BackupViaGitServiceTests.cs` | Service regression tests | Resolver correctness |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | State/action tests | ViewModel contract |
| `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` | Avalonia.Headless UI tests | UI-facing flow coverage |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Conflict detection | Git conflict surfaced as generic sync failure or whole-file spike state | Explicit conflict mode with structured status |
| Resolution granularity | Whole file only | Whole file fallback + field-level choices for supported task JSON |
| Merge semantics | Undefined | Deterministic, tested rules per field type |
| Sync availability | Risk of continuing normal backup actions during conflict | Normal backup actions disabled until resolution completes |
| Testability | No field-level contract | Service/ViewModel/UI test matrix |
| Runner commands | Risk of VSTest `--filter` misuse | TUnit `--treenode-filter` commands documented |

## 18. Альтернативы и компромиссы
- Вариант: keep whole-file only.
  - Плюсы: lowest implementation risk.
  - Минусы: user loses independent task edits; does not satisfy requested interactive merge behavior.
  - Почему не выбран: core user request is field selection and merge.
- Вариант: full free-form JSON editor.
  - Плюсы: maximum flexibility.
  - Минусы: high UI/test complexity, easy to produce invalid or semantically dangerous task JSON.
  - Почему не выбран: v1 needs controlled and testable behavior.
- Вариант: automatic semantic merge without UI.
  - Плюсы: fastest user flow.
  - Минусы: unacceptable data-loss risk for relation deletion/resurrection and scalar conflicts.
  - Почему не выбран: explicit user decision is required for safe release.
- Выбранный вариант: ancestor-aware field resolver with deterministic merge options and whole-file fallback.
  - Плюсы: preserves independent edits, bounded risk, clear tests, safe fallback.
  - Минусы: more implementation/test work and UI density.
  - Почему выбранное решение лучше в контексте этой задачи: it matches product request while keeping release controlled, reversible and testable.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели дизайна и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, API, алгоритмы, интеграции, ошибки и rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Совместимость, rollback, data integrity, scoped staging, raw JSON preservation и path containment зафиксированы. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, test matrix, команды, delete-side behavior и file impact table есть. |
| E. Готовность к автономной реализации | 17-19 | PASS | План этапов, альтернативы и quality gate описаны; блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | Спека соответствует `dotnet-desktop-client`, `ui-automation-testing` и `testing-dotnet`. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Outcome, Non-Goals and stop rules are explicit. |
| 2. Понимание текущего состояния | 5 | Existing Git backup, task JSON storage and test runner constraints are captured. |
| 3. Конкретность целевого дизайна | 5 | Service/ViewModel/UI responsibilities, all-fields display, real-conflict marking and merge rules are specified. |
| 4. Безопасность (миграция, откат) | 5 | No storage migration; rollback via disabling field resolver; path containment, scoped staging and raw token preservation covered. |
| 5. Тестируемость | 5 | Service, ViewModel, UI, delete-side behavior and command matrix are specified. |
| 6. Готовность к автономной реализации | 5 | Ordered phases, acceptance criteria and no blocking questions. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: spec приведена к canonical template; добавлены selected instruction stack, profile compliance, file impact table, before/after mapping, alternatives, quality gate, approval and journal sections; усилено требование three-way ancestor-aware resolver; явно зафиксировано, что текущий code spike должен быть свернут/приведен к spec на EXEC; после review добавлены решения пользователя по all-fields display, delete-side resolution и v1 whole-object handling, а также safety gaps по path containment, raw JSON/date preservation, scoped staging and immutable DTO/UI selection split.
- Что осталось на решение пользователя: требуется только утверждение спеки фразой `Спеку подтверждаю`.

## Approval
Получено: пользователь подтвердил фразой "спеку подтверждаю" 2026-05-10. EXEC разрешён.

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Первичная спецификация интерактивного resolver | 0.78 | Не был проверен canonical template central stack | Сверить с центральными инструкциями | Да, перед EXEC | Да, пользователь указал посмотреть центральный каталог инструкций | Первичный документ зафиксировал риски и алгоритмы, но не полностью соответствовал canonical structure | `specs/2026-05-10-interactive-sync-conflict-resolution.md` |
| SPEC | Сверка central instruction stack | 0.94 | Нет | Привести spec к canonical template и повторить quality gate | Нет | Да, пользователь запросил сверку формата | Локальный `AGENTS.md` указал canonical template and central QUEST rules; требуются секции 0-20, quality gate и журнал | `AGENTS.md`, `C:\Projects\My\Agents\templates\specs\_template.md`, central instructions |
| SPEC | Canonical rewrite + quality gate | 0.92 | Нет | Ожидать `Спеку подтверждаю` перед EXEC | Да | Нет, approval ещё не получен | Спека теперь соответствует central template, содержит linter/rubric/post-SPEC review и задает контролируемый EXEC scope | `specs/2026-05-10-interactive-sync-conflict-resolution.md` |
| SPEC | Review fixes after user decisions | 0.95 | Нет | Ожидать `Спеку подтверждаю` перед EXEC | Да | Да, пользователь решил показывать все измененные поля и использовать удаленную сторону как delete action | Review gaps закрыты в spec: all-fields display с `IsRealConflict`, delete-side resolution, safe paths, raw JSON tokens, scoped staging and DTO/UI split | `specs/2026-05-10-interactive-sync-conflict-resolution.md` |
| SPEC | Object field v1 decision | 0.96 | Нет | Ожидать `Спеку подтверждаю` перед EXEC | Да | Да, пользователь согласился с whole-object v1 behavior | Object fields, включая `Repeater`, зафиксированы как одно поле current/incoming без merge; nested object rows deferred to follow-up | `specs/2026-05-10-interactive-sync-conflict-resolution.md` |
| EXEC | Spike alignment: model and service contract | 0.86 | Нужен targeted test signal | Расширить service/ViewModel/UI tests по spec | Нет | Да, пользователь подтвердил spec | Mutable field DTO заменен immutable service DTO + ViewModel decision state; service resolver стал ancestor-aware, raw-date preserving, path-safe, delete-side capable and no longer stages all files on finish | `src/Unlimotion.ViewModel/BackupConflictStatus.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion/Views/SettingsControl.axaml` |
| EXEC | Service regression coverage | 0.93 | Нужен ViewModel/UI test signal | Обновить ViewModel и Avalonia.Headless tests | Нет | Нет | Добавлены service tests для all-changed-fields + real conflict marker, field selections, unknown fields, raw date strings, invalid merge, delete-side resolution, non-JSON fallback, unresolved-conflict guard and unrelated-file staging; targeted `BackupViaGitServiceTests` прошли 33/33 | `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion/Services/BackupViaGitService.cs` |
| EXEC | ViewModel/UI regression coverage | 0.92 | Нужен финальный build/full-run signal | Запустить solution build и full available tests | Нет | Нет | ViewModel покрывает delete-side availability и field-selection decisions; Avalonia.Headless UI покрывает conflict panel, real-conflict badge, merge radio selection, delete-side incoming action and finish state; targeted `SettingsViewModelTests` прошли 43/43, `SettingsControlResponsiveUiTests` прошли 5/5 | `src/Unlimotion.Test/SettingsViewModelTests.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs`, `src/Unlimotion/Views/SettingsControl.axaml` |
| EXEC | Final validation | 0.88 | Full TUnit run timed out after 20 minutes without runner output | Финальный ответ с результатами и residual risk | Нет | Нет | `dotnet build src/Unlimotion.sln --no-restore /clp:ErrorsOnly` прошёл без ошибок; targeted service/ViewModel/UI suites прошли; полный `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --no-progress` был остановлен по таймауту 20 минут, после чего висящий `dotnet` test process был завершён | `src/Unlimotion.sln`, `src/Unlimotion.Test/Unlimotion.Test.csproj`, текущий журнал спеки |
| EXEC | Responsive modal conflict resolver | 0.9 | Solution-wide sequential build timed out after 10 minutes | Финальный ответ с результатами и residual risk | Нет | Да, пользователь попросил вынести resolver из Settings и поддержать телефон | Resolver вынесен в shared modal control через `DialogHost`, Settings оставляет только статус/кнопку открытия, desktop/tablet layout двухколоночный, phone layout одноколоночный; targeted `SettingsViewModelTests` прошли 43/43, `SettingsControlResponsiveUiTests` прошли 7/7, `Unlimotion.Desktop.csproj` and `Unlimotion.csproj` builds passed | `src/Unlimotion/Views/ConflictResolutionControl.axaml`, `src/Unlimotion/Views/ConflictResolutionControl.axaml.cs`, `src/Unlimotion/Views/MainScreen.axaml`, `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` |
| EXEC | Conflict field context and editable text merge | 0.93 | Нет | Финальный ответ с результатами | Нет | Да, пользователь запросил task identification fields, before-change values, manual text merge and relation titles | Resolver теперь показывает все поля задачи, ancestor value, current/incoming/selected values; text merge fields can be manually edited; relation arrays display known task titles as `Title (id)`; targeted `BackupViaGitServiceTests` прошли 35/35, `SettingsViewModelTests` 43/43, `SettingsControlResponsiveUiTests` 7/7 | `src/Unlimotion.ViewModel/BackupConflictStatus.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion/Views/ConflictResolutionControl.axaml`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` |
| EXEC | Conflict resolver UX polish and completion state | 0.94 | Нет | Финальный ответ с результатами | Нет | Да, пользователь запросил compact non-conflict fields, inline text editor, highlighted finish action and consistent post-completion state | Неконфликтные поля стали compact read-only rows без бесполезных choices; editable text merge replaces selected result instead of duplicating it; finish button visually highlighted; completion clears conflict mode, closes dialog and resumes backup scheduler; targeted `BackupViaGitServiceTests` 35/35, `SettingsViewModelTests` 44/44, `SettingsControlResponsiveUiTests` 7/7, `Unlimotion.Desktop.csproj` build passed | `src/Unlimotion.ViewModel/BackupConflictStatus.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion/Views/ConflictResolutionControl.axaml`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` |
| EXEC | Scheduler safety during conflict resolution | 0.95 | Нет | Обновить PR ветку | Нет | Да, пользователь указал race между Quartz jobs и ручным разрешением конфликтов | Вход в conflict mode теперь паузит Quartz scheduler; `GitPullJob` and `GitPushJob` defensively skip work while `GetConflictStatus().IsInProgress`; added `GitBackupJobTests`; targeted `GitBackupJobTests` 3/3, `BackupViaGitServiceTests` 35/35, `SettingsViewModelTests` 44/44, `Unlimotion.Desktop.csproj` build passed | `src/Unlimotion/App.axaml.cs`, `src/Unlimotion/Scheduling/Jobs/GitPullJob.cs`, `src/Unlimotion/Scheduling/Jobs/GitPushJob.cs`, `src/Unlimotion.Test/GitBackupJobTests.cs`, `specs/2026-05-10-interactive-sync-conflict-resolution.md` |
