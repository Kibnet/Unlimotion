# SPEC: Исполняемый BDD-мост storage/Git settings (SC-0012-002)

## Метаданные и цель
- Профиль: `storm-product-development`; small test-only BDD delivery.
- Baseline: `ST-0012`, `AC-0035`, `GR-035`, `SC-0012-002`.
- Ограничения: не менять production/UI code, `.feature`, existing tests, annotations, external Git/config/user data.
- Цель: связать Settings Gherkin со storage-mode, Git backup и conflict-action evidence; довести `ST-0012` до 2/3 и общий executable ratio до 40/45.

## AS-IS и Evidence
- `SettingsViewModelTests.CanConnectStorage_FollowsSelectedModeRequirements` покрывает local/server storage readiness.
- `SettingsViewModelTests.CanSyncRepository_RequiresBackupRemoteAndPushRefSpecWithoutConnectedState` покрывает Git backup sync readiness.
- `SettingsViewModelTests.ConflictResolutionMode_DisablesSyncAndEnablesSelectedConflictActions` покрывает conflict actions and blocked sync.
- `SettingsControlResponsiveUiTests.SettingsControl_SyncConflictResolutionMode_ShowsOpenResolverAction` покрывает Settings UI action headlessly.
- Сценарий linked-only, без step definitions.

## TO-BE
- Новый `SettingsStorageGitContract` вызывает три existing VM tests через disposable fixture и self-contained headless Settings UI test через fresh non-disposable instance; сохраняет четыре independent flags.
- Новый bridge: `SD-0155..SD-0158`, executable scenario test, minimal `StormScenarioContext` fields, `TS-0065` and STORM artifact/report updates.
- Gherkin/acceptance criteria и existing links `TS-0008/TS-0009` сохраняются.

## Non-Goals и риски
- Нет новых storage/Git/conflict behaviors, visual changes, migrations, UI video, annotations или network access.
- UI test запускается как existing headless evidence; new UI test не нужен, поскольку UI behavior не меняется.
- Full-suite PASS не заявляется из-за historic timeout without summary.
- Review подтверждает, что UI test сам создаёт/закрывает headless session, окно и temporary config; контракт освобождает только `SettingsViewModelTests` fixture.

## Acceptance and Validation
1. Feature text executes through `SD-0155..SD-0158`.
2. Storage readiness, Git readiness, conflict mode and Settings conflict action all pass independently.
3. `SC-0012-002` is `passing`, `ST-0012` is 2/3, metrics 40/45.
4. Build, BDD 1/1, direct VM 3/3, existing headless UI 1/1, validator and `git diff --check` pass.
5. Production/feature/existing annotations diffs stay empty.

Exact TUnit methods: `CanConnectStorage_FollowsSelectedModeRequirements`; `CanSyncRepository_RequiresBackupRemoteAndPushRefSpecWithoutConnectedState`; `ConflictResolutionMode_DisablesSyncAndEnablesSelectedConflictActions`; `SettingsControl_SyncConflictResolutionMode_ShowsOpenResolverAction`.

## Files / Rollback
New: this SPEC, `SettingsStorageGitContract.cs`, `SettingsStorageGitStepDefinitions.cs`, `StormSettingsStorageGitExecutableSpecTests.cs`. Modified: `StormStepDefinition.cs`, `storm.json`, six reports. Rollback removes only new test/artifact files.

## Quality Gate
| Area | Status | Comment |
| --- | --- | --- |
| Scope, evidence, safety, testability | PASS | Exact scenario, files, checks and stop rule are defined |
| Domain | PASS | Storage, Git readiness и conflict actions have independent evidence |
| UX | Не применимо | No UI change |
| Tester | PASS | Individual TUnit filters and self-contained UI lifecycle inspected |
| Developer / delivery | PASS | Fixture-local JSON/headless session only; no external state |

### Post-SPEC Review
- Статус: PASS после исправления.
- Finding fixed: draft implied a disposable UI fixture; inspected class shows self-contained method-level cleanup, and contract boundary is corrected.
- Scope/Evidence, Contract, Adversarial risk and Role-based passes: PASS.
- Decision: active workflow auto-approval permits EXEC.

### Post-EXEC Review
- Статус: PASS.
- Реализация добавляет только test-only contract, четыре step definitions, executable spec и canonical STORM/report links.
- Review: контракт следует уже принятому SettingsAppearanceContract pattern; disposable `SettingsViewModelTests` освобождается в `finally`, а headless UI-проверка не требует fixture cleanup. New executable spec использует существующие `AvaloniaHeadless` serialization attributes.
- Validation: Build Release errors 0 (33 existing warnings); BDD 1/1; VM storage/Git/conflict 3/3; existing headless UI 1/1; final STORM validator и `git diff --check` выполняются перед commit.

## Approval and Log
Автоматическое подтверждение после PASS review; явная фраза: "Спеку подтверждаю".

| Phase | Decision | Next action |
| --- | --- | --- |
| SPEC | Reuse four isolated existing checks | Review, correct, auto-EXEC |
| SPEC review | Clarify UI test lifecycle | Execute test-only bridge |
