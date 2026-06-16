# STORM Traceability Report

Сгенерировано: 2026-06-16
Команда: `/storm:cover ST-0011 ServiceStack API smoke blocker` + `/storm:bdd-sync`

## Test Traceability

| Test | Path | Stories | AC | Scenarios |
|---|---|---|---|---|
| TS-0001 | src/Unlimotion.Test/MainWindowViewModelTests.cs | ST-0001<br>ST-0004<br>ST-0005<br>ST-0013 | AC-0001<br>AC-0002<br>AC-0010<br>AC-0011<br>AC-0013<br>AC-0037<br>AC-0038 | SC-0001-001<br>SC-0001-002<br>SC-0004-001<br>SC-0004-002<br>SC-0005-001<br>SC-0013-001<br>SC-0013-002 |
| TS-0002 | src/Unlimotion.Test/TaskAvailabilityCalculationTests.cs | ST-0003 | AC-0007<br>AC-0008<br>AC-0009 | SC-0003-001<br>SC-0003-002<br>SC-0003-003 |
| TS-0003 | src/Unlimotion.Test/TaskStatus*Tests.cs; src/Unlimotion.Test/FileStorageTaskStatusTests.cs | ST-0002<br>ST-0003<br>ST-0007<br>ST-0009 | AC-0004<br>AC-0005<br>AC-0006<br>AC-0007<br>AC-0009<br>AC-0021<br>AC-0026 | SC-0002-001<br>SC-0002-002<br>SC-0002-003<br>SC-0003-001<br>SC-0003-003<br>SC-0007-003<br>SC-0009-002 |
| TS-0004 | src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs | ST-0001<br>ST-0004<br>ST-0005<br>ST-0013 | AC-0001<br>AC-0003<br>AC-0010<br>AC-0011<br>AC-0012<br>AC-0013<br>AC-0037<br>AC-0038 | SC-0001-001<br>SC-0001-003<br>SC-0004-001<br>SC-0004-002<br>SC-0004-003<br>SC-0005-001<br>SC-0013-001<br>SC-0013-002 |
| TS-0005 | src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs; src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs; src/Unlimotion.Test/MainControlAvailabilityUiTests.cs | ST-0002<br>ST-0003<br>ST-0006<br>ST-0007 | AC-0004<br>AC-0005<br>AC-0007<br>AC-0016<br>AC-0018<br>AC-0019<br>AC-0020<br>AC-0021 | SC-0002-001<br>SC-0002-002<br>SC-0003-001<br>SC-0006-001<br>SC-0006-003<br>SC-0007-001<br>SC-0007-002<br>SC-0007-003 |
| TS-0006 | src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs; src/Unlimotion.Test/MainControlResetFiltersUiTests.cs | ST-0005<br>ST-0008 | AC-0013<br>AC-0014<br>AC-0015<br>AC-0024 | SC-0005-001<br>SC-0005-002<br>SC-0005-003<br>SC-0008-003 |
| TS-0007 | src/Unlimotion.Test/RoadmapGraphUiTests.cs | ST-0008 | AC-0022<br>AC-0023<br>AC-0024 | SC-0008-001<br>SC-0008-002<br>SC-0008-003 |
| TS-0008 | src/Unlimotion.Test/SettingsViewModelTests.cs; src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs | ST-0007<br>ST-0010<br>ST-0012 | AC-0020<br>AC-0028<br>AC-0029<br>AC-0030<br>AC-0034<br>AC-0035<br>AC-0036 | SC-0007-002<br>SC-0010-001<br>SC-0010-002<br>SC-0010-003<br>SC-0012-001<br>SC-0012-002<br>SC-0012-003 |
| TS-0009 | src/Unlimotion.Test/BackupViaGitServiceTests.cs; src/Unlimotion.Test/GitBackupJobTests.cs; src/Unlimotion.Test/GitSafeDirectoryConfigTests.cs | ST-0010<br>ST-0012 | AC-0028<br>AC-0029<br>AC-0030<br>AC-0031<br>AC-0035 | SC-0010-001<br>SC-0010-002<br>SC-0010-003<br>SC-0010-004<br>SC-0012-002 |
| TS-0010 | src/Unlimotion.Test/TaskOutlineClipboardServiceTests.cs; src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs | ST-0013 | AC-0037<br>AC-0038 | SC-0013-001<br>SC-0013-002 |
| TS-0011 | tests/Unlimotion.UiTests.Headless/**/*.cs; tests/Unlimotion.UiTests.FlaUI/**/*.cs; tests/Unlimotion.UiTests.Authoring/**/*.cs | ST-0004<br>ST-0015 | AC-0010<br>AC-0011<br>AC-0012<br>AC-0041<br>AC-0043 | SC-0004-001<br>SC-0004-002<br>SC-0004-003<br>SC-0015-001<br>SC-0015-003 |
| TS-0012 | src/Unlimotion.Test/LocalizationSettingsTests.cs; src/Unlimotion.Test/LocalizationDisplayDefinitionTests.cs | ST-0012 | AC-0034 | SC-0012-001 |
| TS-0013 | src/Unlimotion.Test/MainControlDateQuickSelectionUiTests.cs; src/Unlimotion.Test/MainControlWantedUiTests.cs; src/Unlimotion.Test/TaskImportanceUiTests.cs; src/Unlimotion.Test/TaskItemRepeaterListMarkerTests.cs; src/Unlimotion.Test/TaskListRepeaterMarkerUiTests.cs | ST-0005<br>ST-0006 | AC-0014<br>AC-0016<br>AC-0017<br>AC-0018 | SC-0005-002<br>SC-0006-001<br>SC-0006-002<br>SC-0006-003 |
| TS-0014 | src/Unlimotion.Test/UnifiedTaskStorageMigrationRegressionTests.cs; src/Unlimotion.Test/StartupProjectionAndRelationsTests.cs; src/Unlimotion.Test/TaskMigratorTests.cs; src/Unlimotion.Test/JsonRepairingReaderTests.cs | ST-0001<br>ST-0002<br>ST-0003<br>ST-0009 | AC-0002<br>AC-0006<br>AC-0008<br>AC-0025<br>AC-0026<br>AC-0027 | SC-0001-002<br>SC-0002-003<br>SC-0003-002<br>SC-0009-001<br>SC-0009-002<br>SC-0009-003 |
| TS-0015 | src/Unlimotion.Test/SingleViewStartupUiTests.cs; src/Unlimotion.Test/MainScreenLoadingUiTests.cs; src/Unlimotion.Test/PackageUpdateCompatibilityUiTests.cs; src/Unlimotion.Test/KeyboardAwareScrollViewerUiTests.cs | ST-0012<br>ST-0015 | AC-0036<br>AC-0041<br>AC-0042<br>AC-0043 | SC-0012-003<br>SC-0015-001<br>SC-0015-002<br>SC-0015-003 |
| TS-0016 | src/Unlimotion.Test/BreadcrumbEmojiUiTests.cs; tests/Unlimotion.UiTests.Headless/Tests/ReadmeDemoHeadlessTests.cs | ST-0004 | AC-0011 | SC-0004-002 |
| TS-0017 | src/Unlimotion.Test/ServerStorageBddContractTests.cs | ST-0011 | AC-0032<br>AC-0033 | SC-0011-001<br>SC-0011-002 |
| TS-0018 | src/Unlimotion.Test/ServerStorageBddContractTests.cs | ST-0011 | AC-0033 | SC-0011-002 |
| TS-0019 | src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs | ST-0011 | AC-0033 | SC-0011-002 |

## ST-0011 Trace

| Story | AC | Scenario | Test | Code units | Status |
|---|---|---|---|---|---|
| ST-0011 | AC-0032 | SC-0011-001 | TS-0017 | CU-0003, CU-0012 | passing contract-level evidence |
| ST-0011 | AC-0033 | SC-0011-002 | TS-0017, TS-0018, TS-0019 | CU-0003, CU-0012 | passing contract/security/live SignalR evidence; ServiceStack task API live smoke blocked by ServiceStack free-quota operation registration before endpoint assertions |

## BDD Traceability Layer

| Уровень | Количество |
|---|---:|
| Gherkin Features | 15 |
| Gherkin Rules | 43 |
| Gherkin Scenarios | 43 |
| Scenarios with linked tests | 41 |
| Passing scenarios | 2 |
| Step definitions | 0 |

Trace chain после синхронизации: Vision -> Product Goal -> Need / Constraint -> Story -> Acceptance Criteria -> Gherkin Rule -> Gherkin Scenario -> Test / Step Definition -> Code Unit. Step definitions пока отсутствуют; `ST-0011` связан с TUnit contract, security regression и live SignalR integration tests напрямую. ServiceStack task API live smoke не добавлен в trace как passing test: attempted minimal AppHost startup blocked by ServiceStack free-quota operation registration.
