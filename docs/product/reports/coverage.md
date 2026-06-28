# STORM Coverage Analysis

Сгенерировано: 2026-06-28
Команда: `/storm:cover Android workload install build smoke`
Режим: `delivery-task environment-admin` после подтвержденной SPEC; artifact-only sync, code, tests, `.feature` wording и test annotations не менялись

## Область

Эта итерация выполняет approved environment-admin follow-up для `ST-0015 / AC-0042 / SC-0015-002`: targeted `dotnet workload install android` завершился как no-op, после settled restore/build state Android Debug build smoke прошёл. Browser и iOS build smoke evidence сохранены. Это build-smoke evidence, а не runtime/release support.

Ранее реализованные slices `SC-0011-001 -> SD-0022..SD-0025 -> TS-0031`, `SC-0011-002 -> SD-0022..SD-0024 + SD-0026 -> TS-0032`, `SC-0015-002 -> SD-0001..SD-0004 -> TS-0026`, `SC-0014-002 -> SD-0005..SD-0008 -> TS-0027`, `SC-0014-001 -> SD-0009..SD-0012 -> TS-0028`, `SC-0014-003 -> SD-0013..SD-0016 -> TS-0029` и `SC-0016-001 -> SD-0017..SD-0021 -> TS-0030` сохранены.

Acceptance criteria не заменялись на Gherkin. Существующие stories, tests, conflicts, dependencies и решение по `CV-0007` сохранены.

Эта environment-admin итерация не меняла behavior coverage metrics: сценарии, Gherkin rules, step definitions и Scenario -> Test links не добавлялись. Предыдущий full-suite gate остаётся восстановленным по commit `5fcb1a2`: full `Unlimotion.Test` проходил 563/563 вне sandbox; в этой SPEC full-suite не запускался.

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

## Результат Android/iOS Workload Repair

| Item | Результат | Классификация |
| --- | --- | --- |
| Workload state before targeted install | `dotnet workload list` показывает workload version `10.0.301.1`; `android 36.1.69/10.0.100` listed with sources `SDK 10.0.300` + VS. | environment snapshot |
| Targeted Android workload install | `dotnet workload install android` завершился exit 0: изменений workload не найдено, `android` уже установлен. | install_noop |
| Workload state after targeted install | `dotnet workload list` unchanged: Android/iOS/wasm-tools listed, workload version `10.0.301.1`. | unchanged_environment_state |
| Android build smoke | `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug -v minimal` проходит за 00:00:08.66, warnings 4, errors 0, produces `bin\Debug\net10.0-android\Unlimotion.Android.dll`. | passed_build_smoke |
| iOS build smoke | `dotnet build src\Unlimotion.iOS\Unlimotion.iOS.csproj -c Debug` прошёл ранее 2026-06-28. | passed_build_smoke |

## Оставшиеся Partial AC

Нет.

## Coverage Backlog

| ID | Target | Status | Tests / Minimal tests | Результат |
| --- | --- | --- | --- | --- |
| CV-0001 | AC-0032 / ST-0011 | covered_by_contract_tests_and_executable_bdd | TS-0017, TS-0031 | Auth flow получил passing contract-level evidence и `SC-0011-001` step-executable. |
| CV-0002 | AC-0033 / ST-0011 | covered_by_live_task_api_signalr_tests_and_executable_bdd | TS-0017, TS-0018, TS-0019, TS-0020, TS-0032 | ServiceStack task API и SignalR live paths покрыты; `SC-0011-002` step-executable. |
| CV-0003 | AC-0039 / ST-0014 | covered_by_telegram_command_auth_tests | TS-0022, TS-0028 | Command/auth покрыты; `SC-0014-001` step-executable. |
| CV-0004 | AC-0040 / ST-0014 | covered_by_telegram_callback_and_timer_tests | TS-0023, TS-0025, TS-0027, TS-0029 | Callback behavior и Git timer conflict-safety покрыты; `SC-0014-002` и `SC-0014-003` step-executable. |
| CV-0005 | AC-0042 / ST-0015 | covered_by_project_contract_tests | TS-0024, TS-0026 + Browser/iOS/Android build smoke | Browser build smoke подтвержден; `SC-0015-002` step-executable; iOS Debug build smoke прошёл 2026-06-28; Android Debug build smoke прошёл 2026-06-28; runtime release claim не заявляется. |
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
| `dotnet workload list` before targeted install | workload version `10.0.301.1`; Android `36.1.69/10.0.100` listed with sources SDK `10.0.300` + VS |
| `dotnet workload install android` | exit 0; workload update check completed; no workload changes found; `android` already installed |
| `dotnet workload list` after targeted install | unchanged workload list, workload version `10.0.301.1` |
| `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug` | first post-install build restored packages and returned exit 1 without final error captured; workload blocker was no longer `NETSDK1147` at project start |
| `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug --no-restore -v minimal` | passed; produced `src\Unlimotion.Android\bin\Debug\net10.0-android\Unlimotion.Android.dll`; warnings include NU1608, CA1416, XA0141, XA4301 |
| `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug -v minimal` | passed in 00:00:08.66; warnings 4, errors 0 |
| `dotnet build-server shutdown` | MSBuild and compiler servers shut down |
| `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` | OK: 0 errors, 1 warning; warning is duplicate shared Given step text across `SD-0009`, `SD-0013`, `SD-0022` |
| `git diff --check` | passed with no whitespace errors |
| `rg -n "[ \t]+$" docs\product ...` | no trailing whitespace matches |
| `Get-Process dotnet,msiexec` | no `dotnet`/`msiexec` processes found after build-server shutdown |
| `git status --short --untracked-files=all` | existing artifact-only sync preserved; code/tests/workflows unchanged |
| `dotnet --info` | before repair: SDK `10.0.301`, workload version `10.0.300-manifests.6fc1bb7b`; workloads via VS/MSI; workload sets not installed |
| `dotnet workload list` before repair | Android/iOS/MacCatalyst/MAUI Windows/wasm-tools listed via VS, workload version `10.0.300-manifests.6fc1bb7b` |
| `dotnet workload restore src\Unlimotion.Android\Unlimotion.Android.csproj --verbosity normal` | escalated approved command timed out after 184s with no captured stdout; subsequent workload list shows workload set `10.0.301.1` |
| `dotnet workload list` after repair attempt | workload version `10.0.301.1`; Android `36.1.69`, iOS `26.5.10284`, wasm-tools `10.0.109`; sources include SDK `10.0.300` + VS |
| `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug` | failed `NETSDK1147` before compile; required workload: `android` |
| `dotnet build src\Unlimotion.iOS\Unlimotion.iOS.csproj -c Debug` | passed in 00:00:29.82 with existing warnings CS0618, SYSLIB0014, AVLN5001 |
| `dotnet build-server shutdown` | MSBuild and compiler servers shut down; lingering dotnet/msiexec environment processes observed and not forcibly killed |
| `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` | OK: 0 errors, 1 warning по intentional shared Given step text |
| `git diff --check` | passed |
| `rg -n "[ \t]+$" docs\product specs\2026-06-26-storm-android-ios-build-smoke-workload-setup.md specs\2026-06-27-storm-android-ios-workload-set-repair.md` | no matches (`rg` exit 1) |
| `git status --short` | перед environment/setup только untracked approved SPEC; code/tests/workflows clean |
| `dotnet --info` | SDK `10.0.301`, workload version `10.0.300-manifests.6fc1bb7b`; workload sets not installed; installed workloads include Android/iOS/wasm-tools |
| `dotnet workload list` | Android, iOS, MacCatalyst, MAUI Windows и wasm-tools установлены via VS/MSI, но workload set отсутствует |
| `dotnet workload restore src\Unlimotion.Android\Unlimotion.Android.csproj` | failed while installing workload set `10.0.301.1` / `microsoft.net.workloads.10.0.300.msi.x64`; operation canceled; rollback; stop rule triggered |
| `dotnet workload restore src\Unlimotion.iOS\Unlimotion.iOS.csproj` | не запускался после Android restore blocker, чтобы не повторять interactive/system install path |
| `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug` | failed `NETSDK1147` before project compile; suggested workload restore for `wasm-tools` |
| `dotnet build src\Unlimotion.iOS\Unlimotion.iOS.csproj -c Debug` | failed `NETSDK1147` before project compile; suggested workload restore for `wasm-tools` |
| `dotnet workload list` after restore attempt | installed workload list unchanged |
| `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` | OK: 0 errors, 1 warning по intentional shared Given step text |
| `git diff --check` | passed |
| `rg -n "[ \t]+$" docs\product specs\2026-06-26-storm-android-ios-build-smoke-workload-setup.md` | no matches (`rg` exit 1) |
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
2. Browser, iOS и Android build smoke evidence есть для `SC-0015-002`; runtime launch, emulator/device validation и release pipeline evidence не заявлены.
3. Full-suite validation gate восстановлен предыдущей итерацией: текущий full `Unlimotion.Test` проходил 563/563 вне sandbox; эта environment-admin SPEC full-suite не запускала.
4. `CV-0007` не является active cover gap после Варианта B.

## Рекомендуемый Следующий Шаг

Следующий осмысленный шаг для продолжения `/storm:cover`: либо отдельная runtime/release SPEC для platform launch/package evidence, либо выбор следующего high-value scenario для executable BDD coverage. Repo config/tests/code менять нельзя без отдельной delivery SPEC. `CV-0007` остается internal/orphan candidate до нового решения.
