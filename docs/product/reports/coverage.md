# STORM Coverage Analysis

Сгенерировано: 2026-06-26
Команда: `/storm:cover validation stabilization / Windows ACL hardening full-suite`
Режим: `delivery-task` после подтвержденной SPEC; production security hardening изменен, tests, `.feature` wording и test annotations не менялись

## Область

Эта итерация добавляет executable BDD slice для `ST-0011 / AC-0033 / SC-0011-002`: сценарий исполняется из `.feature` текста через shared repo-local step definitions `SD-0022..SD-0024`, новый Then step `SD-0026` и TUnit evidence `TS-0032`. Existing evidence `TS-0017`, `TS-0018`, `TS-0019` и `TS-0020` сохранено и повторно прошло через reusable CRUD/SignalR contract.

Ранее реализованные slices `SC-0011-001 -> SD-0022..SD-0025 -> TS-0031`, `SC-0015-002 -> SD-0001..SD-0004 -> TS-0026`, `SC-0014-002 -> SD-0005..SD-0008 -> TS-0027`, `SC-0014-001 -> SD-0009..SD-0012 -> TS-0028`, `SC-0014-003 -> SD-0013..SD-0016 -> TS-0029` и `SC-0016-001 -> SD-0017..SD-0021 -> TS-0030` сохранены.

Acceptance criteria не заменялись на Gherkin. Существующие stories, tests, conflicts, dependencies и решение по `CV-0007` сохранены.

Эта validation-итерация не меняла behavior coverage metrics: сценарии, Gherkin rules, step definitions и Scenario -> Test links не добавлялись. Закрыт full-suite blocker в Windows ACL hardening для configured SSH private key: production hardening теперь очищает non-whitelisted explicit ACL entries через `PurgeAccessRules` на существующем security descriptor и отключает наследование. Targeted ACL tests проходят, targeted Headless reset risk проходит, full `Unlimotion.Test` проходит 563/563 вне sandbox.

## Сводка

| Метрика | Значение |
| --- | --- |
| Acceptance criteria всего | 44 |
| AC с тестовыми связями | 44 |
| AC с уровнем full/critical | 44 |
| AC с уровнем partial | 0 |
| AC без тестовых связей | 0 |
| Active cover/behavior gaps | 0 |
| Scenario -> Test links | 45/45 |
| Draft scenarios | 0 |
| Passing scenarios | 7 |
| Step definitions | 26 |
| Step-executable scenarios | 7/45 |

## Результат SC-0011-002 Executable Slice

| Item | Было | Стало | Evidence |
| --- | --- | --- | --- |
| `SC-0011-002.step_definitions` | `[]` | `SD-0022`, `SD-0023`, `SD-0024`, `SD-0026` | `StormServerStorageCrudRealtimeExecutableSpecTests` исполняет шаги из `features/storm/st-0011-server-storage.feature`. |
| `SC-0011-002.linked_tests` | `TS-0017..TS-0020` | `TS-0017..TS-0020`, `TS-0032` | `TS-0032` связывает Gherkin scenario с reusable CRUD/SignalR contract. |
| `CV-0002 / AC-0033` | live API and SignalR evidence | live API and SignalR evidence + executable BDD slice | `TS-0017..TS-0020` покрывают contract/security/live paths; `TS-0032` покрывает executable BDD path. |

## Оставшиеся Partial AC

Нет.

## Coverage Backlog

| ID | Target | Status | Tests / Minimal tests | Результат |
| --- | --- | --- | --- | --- |
| CV-0001 | AC-0032 / ST-0011 | covered_by_contract_tests_and_executable_bdd | TS-0017, TS-0031 | Auth flow получил passing contract-level evidence и `SC-0011-001` step-executable. |
| CV-0002 | AC-0033 / ST-0011 | covered_by_live_task_api_signalr_tests_and_executable_bdd | TS-0017, TS-0018, TS-0019, TS-0020, TS-0032 | ServiceStack task API и SignalR live paths покрыты; `SC-0011-002` step-executable. |
| CV-0003 | AC-0039 / ST-0014 | covered_by_telegram_command_auth_tests | TS-0022, TS-0028 | Command/auth покрыты; `SC-0014-001` step-executable. |
| CV-0004 | AC-0040 / ST-0014 | covered_by_telegram_callback_and_timer_tests | TS-0023, TS-0025, TS-0027, TS-0029 | Callback behavior и Git timer conflict-safety покрыты; `SC-0014-002` и `SC-0014-003` step-executable. |
| CV-0005 | AC-0042 / ST-0015 | covered_by_project_contract_tests | TS-0024, TS-0026 + Browser Release build smoke | Browser build smoke подтвержден; `SC-0015-002` step-executable; Android/iOS build smoke blocked by `NETSDK1147`; runtime release claim не заявляется. |
| CV-0006 | PRODUCT-ENTRY / ST-0016 | covered_by_product_story_existing_ui_test_and_executable_bdd | TS-0021, TS-0030 | Error-toast behavior связан с product story и `SC-0016-001` step-executable. |
| CV-0007 | PRODUCT-ENTRY / proposed_attachment_workflow | internal_orphan_contract_candidate | no active cover link | Вариант B: attachment code сохранен как internal/orphan candidate. |

## BDD Behavior Coverage

| Метрика | Значение |
| --- | --- |
| Feature files | 16 |
| Gherkin Rules | 44 |
| Gherkin Scenarios | 45 |
| Active stories со scenarios | 16/16 |
| AC со Gherkin rules | 44/44 |
| AC со Gherkin scenarios | 44/44 |
| Automated or passing scenarios | 45 |
| Draft scenarios | 0 |
| Passing scenarios | 7 |
| Failing scenarios | 0 |
| Scenarios with linked tests | 45/45 |
| Step definitions | 26 |
| Step-executable scenarios | 7/45 |
| Executable specification ratio | 7/45 step-executable; 7/45 passing scenarios |

## Validation Evidence

| Проверка | Результат |
| --- | --- |
| `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore` | first attempt failed because stale `Unlimotion.Test (33028)` locked output DLLs; stale test host stopped; rerun passed with existing warnings |
| `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore` | rerun after `TS-0032` `NotInParallel` annotation passed with existing warnings |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*" --output Detailed` | прошло 1/1 |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*" --output Detailed` | rerun after `TS-0032` `NotInParallel` annotation прошло 1/1 |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageAuthExecutableSpecTests/*" --output Detailed` | прошло 1/1 |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed` | прошло 7/7 |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed` | прошло 2/2 |
| `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore` | прошло с существующими warnings |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask" --output Detailed` | initial isolated run failed on `pasted=false`; after test-only focus setup and rebuild passed 1/1 |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/*" --output Detailed` | прошло 43/43 |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed` | process failed after 193 passing tests, 0 failed assertions, exit `-532462766`; blocker: unobserved ServiceStack/FileSystemWatcher cleanup exception logs through disposed `EventLogInternal` after `LiveServiceStackTaskApiNarrowTest` |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed` | targeted rerun after full-suite blocker passed 2/2 |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*" --output Detailed` | targeted rerun after full-suite blocker passed 1/1 |
| `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore` | 2026-06-26 passed with existing warnings |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed` | 2026-06-26 прошло 2/2 после live host cleanup stabilization |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*" --output Detailed` | 2026-06-26 прошло 1/1 после live host cleanup stabilization |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed` | 2026-06-26 full-suite completed normally: 563 total, 561 passed, 2 failed; previous `ServerContentRoot`/`EventLogInternal` process crash not reproduced |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/BackupViaGitServiceTests/GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows" --output Detailed` | 2026-06-26 failed 1/1: `BUILTIN\Users` ACL assertion remains true when inherited rules are included |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/ResetFiltersButton_IsAvailableOnTaskTabs" --output Detailed` | 2026-06-26 targeted rerun passed 1/1; full-suite Headless dispose failure is order-dependent |
| `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore` | 2026-06-26 прошло с существующими warnings; Windows-only ACL helper annotations убрали новый CA1416-шум из измененного кода |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/BackupViaGitServiceTests/GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows" --output Detailed` | 2026-06-26 прошло 1/1 вне sandbox с реальным Windows ACL token |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/BackupViaGitServiceTests/GenerateManagedRsaSshKey_HardensPrivateKeyPermissionsOnWindows" --output Detailed` | 2026-06-26 прошло 1/1 |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/ResetFiltersButton_IsAvailableOnTaskTabs" --output Detailed` | 2026-06-26 прошло 1/1 |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed` | 2026-06-26 full-suite прошло 563/563 вне sandbox; предыдущие ACL и Headless dispose blockers не воспроизведены |
| `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` | 2026-06-26 OK: 0 errors, 1 warning по intentional shared Given step text |
| `git diff --check` | 2026-06-26 passed; only LF-to-CRLF working-copy warnings |
| `rg -n "[ \t]+$" src\Unlimotion src\Unlimotion.Test docs\product specs\2026-06-26-storm-stabilize-backup-acl-full-suite.md` | 2026-06-26 no matches (`rg` exit 1) |
| `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` | OK: 0 errors, 1 warning по intentional shared Given step text |
| `git diff --check` | passed; only LF-to-CRLF working-copy warnings |
| `rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-24-storm-stabilize-full-suite-ui-state-order.md specs\2026-06-25-storm-stabilize-servicestack-live-host-cleanup.md` | no matches (`rg` exit 1) |

## Оставшиеся Gaps

1. Step definitions покрывают `SC-0011-001`, `SC-0011-002`, `SC-0015-002`, `SC-0014-002`, `SC-0014-001`, `SC-0014-003` и `SC-0016-001`: остальные scenarios пока rely on linked TUnit evidence.
2. Android/iOS build smoke требует отдельной environment/setup task из-за `NETSDK1147`; runtime smoke и release pipeline evidence не заявлены.
3. Full-suite validation gate восстановлен: текущий full `Unlimotion.Test` проходит 563/563 вне sandbox; ACL false-negative внутри sandbox не используется как product validation signal.
4. `CV-0007` не является active cover gap после Варианта B.

## Рекомендуемый Следующий Шаг

Следующий осмысленный шаг для продолжения `/storm:cover`: либо отдельная environment/setup SPEC для Android/iOS build smoke по `NETSDK1147`, либо выбор следующего high-value scenario для executable BDD coverage после product decision. `CV-0007` остается internal/orphan candidate до нового решения.
