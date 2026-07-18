# Восстановление полного TUnit-прогона после STORM coverage

## 0. Метаданные
- Тип (профиль): `delivery-task`; `storm-product-development`, `dotnet-desktop-client`, `testing-dotnet`, локальный UI-testing override.
- Владелец: Codex.
- Масштаб: large: один итоговый full-suite gate, включающий четыре локальных contract fix-а и cross-cutting миграцию 155 fixture-cleanup call sites.
- Целевое семейство / behavior baseline: текущий `storm-bootstrap` поверх `origin/main` (`5aebebc`), без изменения пользовательского поведения.
- Поверхность: Не применимо. Это локальная .NET/Avalonia delivery-задача.
- Effective runtime: .NET 10.0.10, TUnit 1.44.0.0, Microsoft Testing Platform 2.2.2, Windows x64.
- Eval baseline / evidence: последовательный полный TUnit-прогон `18.07.2026`: 675 всего, 666 успешных, 9 сбоев, 0 пропусков, 22м 35,760с; stdout/TRX/HTML в `C:\tmp\unlimotion-full-clean-20260718-4e2528967c1e4647a3462502f9cc9fe0`.
- Проверенный targeted-run contract: `dotnet test ... -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*"` исполнил ровно 2/2 теста `18.07.2026`; `--list-tests` в TUnit 1.44 выводит полный discovery list и не используется как доказательство selection.
- Целевой релиз / ветка: `storm-bootstrap`.
- Ограничения: не ослаблять продуктовые assertions, не скрывать ошибки через skip/retry/timeout, не менять acceptance criteria или Gherkin-смысл, не менять production UI-поведение. Не использовать общий timeout для полного прогона.
- Связанные ссылки: STORM BDD tests `SC-0011-*`, `SC-0015-*`, `SC-0005-002`; full-run evidence выше.

## 1. Overview / Цель
Восстановить проверяемый зелёный full-suite gate после STORM BDD coverage и rebase на актуальный `main`.

Outcome contract:
- Success means: полный последовательный TUnit-прогон завершает 675+ тестов без failed/skipped, без `Unobserved task exception` в stderr и без тестов дольше трёх минут.
- Итоговый артефакт / output: исправленные mapping/test/lifecycle contracts, актуальные STORM evidence и новый TRX/HTML full-run report.
- Stop rules: остановиться и запросить отдельное решение, если исправление требует изменения пользовательского поведения, public API или миграции persisted task data.

## 2. Текущее состояние (AS-IS)
- `AppModelMapping.ConfigureMapping()` валидирует два inbound map-а `TaskItemMold -> TaskItem` и `TaskItemHubMold -> TaskItem`. После добавления `TaskItem.ExtensionData` они падают на AutoMapper validation.
- Веточные executable contracts для Android/Browser и server auth проверяют удалённые строковые API (`TaskStorageFactory.*`, `RefreshToken(settings, configuration!)`), хотя текущая реализация использует `UnlimotionClientOptions`, `App.ConfigureFileStoragePathPreparation` и `PersistSettings`.
- `FilterResetUiContract` проверяет `vm.StatusFilters` даже после reset-а Last Created и Unlocked, хотя с `6cbcba7` status filters изолированы по вкладкам.
- `MainWindowViewModelFixture.CleanTasks()` удаляет временные файлы синхронно. Full run зарегистрировал 7 `Unobserved task exception` с `FileNotFoundException` из отложенных autosave task-ов после удаления fixture directory.
- На baseline самый долгий тест: `TaskGraphWorkspaceCommandScenario_ExecutesFeatureSteps`, 38,382с. Тестов дольше 180с нет.

## 3. Проблема
После объединения текущей ветки с актуальным `main` full-suite verification gate не является достоверно зелёным: 9 тестов воспроизводимо падают, а teardown оставляет фоновые файловые исключения. Это один delivery-outcome: ветка не доказала корректность полного набора. Внутри outcome есть отдельный cross-cutting lifecycle work package; он не может быть заменён timeout/retry или частичным cleanup fix-ом.

## 4. Цели дизайна
- Сохранить текущие продуктовые semantics server storage, platform startup и per-tab filters.
- Исправить AutoMapper configuration на границе transport model -> domain model без сериализации `ExtensionData` в transport contract.
- Сделать executable/BDD contracts точными, а не завязанными на переименованный внутренний API.
- Завершать autosave lifecycle до удаления временного хранилища и наблюдать все faults.
- Доказать результат полным последовательным прогоном с детальным потоковым выводом.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем UX, фильтрацию задач, правила авторизации или platform startup behavior.
- Не удаляем STORM stories, scenarios, tests или Gherkin tags.
- Не сокращаем тесты искусственными timeout-ами и не помечаем их skipped.
- Не оптимизируем тесты быстрее 3 минут: baseline не содержит таких тестов.
- Не меняем external server API, persisted task schema или migration path.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `AppModelMapping` -> явно исключает domain-only `ExtensionData` из inbound transport maps.
- `PlatformShellProjectContracts` и `ServerStorageAuthContract` -> проверяют актуальный наблюдаемый startup/auth contract, не исторические implementation strings.
- `FilterResetUiContract` -> активирует и проверяет status filters конкретной выбранной вкладки.
- `TaskItemViewModel` и `MainWindowViewModelFixture` -> герметично завершают pending autosave перед удалением fixture storage.
- `docs/product/storm.json` и BDD reports -> фиксируют актуальный passing evidence без изменения story/AC/Gherkin semantics.

### 6.2 Детальный дизайн
1. В `TaskItemMold -> TaskItem` и `TaskItemHubMold -> TaskItem` добавить явное игнорирование `TaskItem.ExtensionData` совместно с existing computed-status ignores. `ExtensionData` остаётся JSON forward-compatibility данными domain persistence и не становится частью server transport mapping.
2. Заменить brittle source-string assertions platform shell и server auth на assertions актуальных contracts:
   - Android: `App.Init` с `UnlimotionClientOptions.PrepareFileStoragePathAsync` и повторная настройка через `App.ConfigureFileStoragePathPreparation`.
   - Browser: `App.Init` с `UnlimotionClientOptions.DefaultTaskStoragePath`, Avalonia/ReactiveUI startup и `AppBuilder.Configure<App>()`.
   - Server storage: password login, refresh через `RefreshToken(settings)`, registration fallback и `PersistSettings` для refresh-token persistence.
3. В `FilterResetUiContract` сделать setup/verification status filters scoped к tab collection (`StatusFilters`, `LastCreatedStatusFilters`, `UnlockedStatusFilters`). Проверка должна доказать reset изменённого фильтра текущей вкладки и сохранить независимость соседних вкладок.
4. Устранить fixture teardown race через следующий обязательный lifecycle contract:
   - `TaskItemViewModel` получает internal-only sealing API для test cleanup. В том же `_pendingSavesLock` он запрещает admission новых autosave, создаёт и кэширует единственный snapshot/drain task для уже зарегистрированных saves. `ExecuteSaveCommand` обязан проверять seal до запуска и регистрации save; после seal новый autosave не запускается.
   - После dispose VM producers fixture seals каждый доступный `TaskItemViewModel`, создаёт один outer `Task.WhenAll` для всех drain tasks и ожидает его до dispose storage/config и удаления файлов. Fixture захватывает, но не теряет drain fault; cleanup storage/files выполняется в `finally`, после чего один раз surface-ится flattened aggregate (`AggregateException.Flatten().InnerExceptions`) save/delete failures. Ни один save fault не должен остаться unobserved.
   - `MainWindowViewModelFixture` становится `IAsyncDisposable` с `CleanTasksAsync()` и общим cached cleanup task. Повторные `CleanTasksAsync()`/`DisposeAsync()` await-ят тот же task, не выполняют directory delete повторно и не используют sync-over-async.
   - Все текущие 155 `CleanTasks()` call sites в 28 test files переводятся на `await fixture.CleanTasksAsync()` или `await using`/`IAsyncDisposable`. Если вызов находится в синхронном teardown, сам test fixture переводится на async teardown; blocking wrapper запрещён. После миграции `rg -n '\.CleanTasks\(\)' src/Unlimotion.Test -g '*.cs'` не возвращает совпадений.
   - Новый `MainWindowViewModelFixtureLifecycleTests` фиксирует: drain до удаления storage, aggregation нескольких save failures без `Unobserved task exception`, idempotent async cleanup без Avalonia UI deadlock. Тест восстанавливает затронутое global/scheduler state в `finally`.
5. После baseline failure и после final green evidence синхронизировать только status/evidence links существующих STORM scenarios/tests. Gherkin wording, story/AC IDs и test annotations не меняются. State-transition table в разделе 11 определяет точные scenario/test/report changes для `/storm:bdd-sync` и `/storm:bdd-lint`.

Visual planning artifact: Не применимо. Продуктовый UI не меняется; изменяется headless test contract для уже существующего поведения.

UI test video evidence: Не применимо. Затрагивается существующий Avalonia.Headless scenario, а не интерактивный UI delivery flow; detailed TUnit/HTML evidence является применимым fallback.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Полный quality gate | Запуск full suite | 0 failed, 0 skipped, нет background teardown errors | TRX, HTML, merged console log | AC-1, AC-6 |
| Сброс фильтров | Пользователь сбрасывает filters на All Tasks, Last Created или Unlocked | Сбрасывается только выбранная вкладка до её defaults | `StormFilterResetExecutableSpecTests` | AC-4 |
| Server storage | Запуск live CRUD/auth scenario | Host стартует и mapping валиден | targeted server tests и BDD scenarios | AC-2 |
| Platform shells | Запуск platform contract scenario | Android/Browser startup contracts подтверждают current API | targeted platform tests и BDD scenario | AC-3 |
| Fixture teardown | Test cleanup после delayed/faulted autosave | Save завершён или его fault наблюдён до удаления fixture storage | `MainWindowViewModelFixtureLifecycleTests`, stderr/console scan | AC-5 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Вкладка с changed status filters | Reset | Только collection выбранной вкладки возвращается к defaults | Другие tab collections не перезаписываются | UI behavior уже существует |
| Autosave в полёте | `CleanTasksAsync` | Dispose producers, atomically seal every task, await one outer drain, только затем dispose/delete | File storage уже закрыт/несколько save failures/повторный cleanup | Все fault-ы агрегированы; new save после seal не запускается |
| Unknown JSON fields в domain task | Server mapping startup | Mapper validation проходит, transport model не получает field | `ExtensionData = null` | Совместимость persistence сохраняется |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Scope | agent | Восстановить полный gate, а не только устранить >3m tests | 0.97 | Остаются известные красные тесты | Нет |
| Mapping policy | agent | Ignore domain persistence `ExtensionData` в inbound server maps | 0.96 | Поле ошибочно станет transport contract | Нет |
| Contract repair | agent | Обновить tests к current observable API, не возвращать obsolete API | 0.95 | Слишком слабая проверка | Нет |
| Targeted TUnit selection | agent | `dotnet test` с legacy `--` separator; итог exact-count подтверждается summary, а не `--list-tests` | 1.00 | Незапланированно запускается весь suite | Нет |
| Teardown policy | agent | Dispose producers -> same-lock seal -> one outer drain -> aggregate -> dispose/delete; async-only migration 155 calls | 0.96 | Долгий cleanup/deadlock/потерянный fault | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Server mapping | `AppModelMapping` + domain `TaskItem` | Ignore non-transport extension data inbound | Нет migration | Live integration / BDD server tests |
| Shell startup | Android/Browser sources | Contract tests assert current startup seam | Нет product config change | Platform contract tests |
| Auth persistence | `ServerStorage` | Contract test checks `PersistSettings` seam | Existing source settings remain supported | Auth contract/BDD tests |
| Autosave lifecycle | `TaskItemViewModel` + `MainWindowViewModelFixture` | Internal seal state, cached drain/cleanup task, async call-site migration | Нет data migration или public API change | 3 lifecycle regressions + `rg` inventory + full console/TRX scan |

## 7. Бизнес-правила / Алгоритмы (если есть)
- Status filters are scoped per tab: reset must target exactly the selected tab collection.
- `ExtensionData` is preservation metadata for JSON domain persistence; it is not an authenticated server DTO field.
- Test fixture cleanup may delete storage only after it has disposed producers, atomically prevented new save admission and observed every pending save outcome.
- `CleanTasksAsync` is the only cleanup API after migration; it is idempotent and may surface an aggregate of save/cleanup failures, but never hides them in a fire-and-forget continuation.

## 8. Точки интеграции и триггеры
- AutoMapper validation runs when the live test host creates its DI container.
- Reset command invokes `MainWindowViewModel.ResetCurrentTabFilters` through the existing confirmation flow.
- Autosave tracking runs from `TaskItemViewModel.ExecuteSaveCommand`; fixture cleanup owns the final seal/drain before filesystem deletion and awaits it from TUnit async teardown.

## 9. Изменения модели данных / состояния
Нет новых persisted fields. Добавляются private lifecycle state (`isPendingSavesSealed`, cached sealed drain task и cached fixture cleanup task); он не сериализуется, не меняет public domain model и доступен test fixture только через existing `InternalsVisibleTo("Unlimotion.Test")` boundary.

## 10. Миграция / Rollout / Rollback
- Migration: не требуется.
- Rollout: обычный merge после green full suite.
- Rollback: revert одного cohesive recovery commit; продуктовые данные и external API не изменяются.

## 11. Тестирование и критерии приёмки
- AC-1: Full sequential TUnit run: 0 failed, 0 skipped, no `Unobserved task exception`; output continues to stream and no `UnitTestResult.duration` is longer than 180s.
- AC-2: оба live server-storage tests, CRUD realtime BDD scenario, один specified auth contract и auth BDD scenario проходят; mapper startup validates; каждый targeted command возвращает ровно expected count из таблицы selection.
- AC-3: все три Android/Browser/iOS platform contract tests и `PlatformShellContractScenario` проходят against the current implementation; counts match the selection table.
- AC-4: `FilterResetScenario` проходит в isolation и full run; checks all relevant per-tab filter collections; count is exactly 1.
- AC-5: три `MainWindowViewModelFixtureLifecycleTests` наблюдают multi-save failure/finish до удаления storage, не оставляют `Unobserved task exception`, являются idempotent и не создают UI deadlock; в `src/Unlimotion.Test` не остаётся synchronous `.CleanTasks()` call sites.
- AC-6: `storm.json` и четыре named reports retain existing traceability, добавляют baseline/final evidence только для таблицы scenario transitions; `/storm:bdd-sync`, `/storm:bdd-lint` и validator pass without hiding known warnings.

Targeted selection contract: до трактовки результата команды как evidence её итоговая TUnit summary обязана содержать ровно expected count ниже. `--minimum-expected-tests` предотвращает недовыбор; summary count предотвращает случайное расширение selection. `--list-tests` не является заменой этому контракту.

| Gate | TUnit filter | Expected count | Scenario/test evidence |
| --- | --- | ---: | --- |
| Live server mapping | `/*/*/ServerStorageLiveIntegrationTests/*` | 2 | `TS-0019`, `TS-0020`, `SC-0011-002` |
| CRUD realtime BDD | `/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*` | 1 | `TS-0032`, `SC-0011-002` |
| Server auth contract | `/*/*/ServerStorageBddContractTests/ServerStorage_Connect_UsesLoginRegisterAndRefreshTokenFlow` | 1 | `TS-0017`, `SC-0011-001` |
| Server auth BDD | `/*/*/StormServerStorageAuthExecutableSpecTests/*` | 1 | `TS-0031`, `SC-0011-001` |
| Platform project contract | `/*/*/PlatformShellProjectContractTests/*` | 3 | `TS-0024`, `SC-0015-002` |
| Platform BDD | `/*/*/StormPlatformShellExecutableSpecTests/*` | 1 | `TS-0026`, `SC-0015-002` |
| Filter reset BDD | `/*/*/StormFilterResetExecutableSpecTests/*` | 1 | `TS-0033`, `SC-0005-002` |
| Fixture lifecycle | `/*/*/MainWindowViewModelFixtureLifecycleTests/*` | 3 | New focused regression coverage; technical reliability constraint |

Команды проверки:
```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --minimum-expected-tests 2 --maximum-parallel-tests 1 --output Detailed --no-ansi
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed --no-ansi
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageBddContractTests/ServerStorage_Connect_UsesLoginRegisterAndRefreshTokenFlow" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed --no-ansi
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageAuthExecutableSpecTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed --no-ansi
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/PlatformShellProjectContractTests/*" --minimum-expected-tests 3 --maximum-parallel-tests 1 --output Detailed --no-ansi
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/StormPlatformShellExecutableSpecTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed --no-ansi
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/StormFilterResetExecutableSpecTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed --no-ansi
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelFixtureLifecycleTests/*" --minimum-expected-tests 3 --maximum-parallel-tests 1 --output Detailed --no-ansi
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
```
Финальный full run запускается без `--timeout`; merged stdout/stderr остаётся в terminal и одновременно сохраняется в отдельный run directory вне рабочего дерева:

```powershell
$runRoot = Join-Path $env:TEMP ("unlimotion-full-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$consoleLog = Join-Path $runRoot "console.log"

& dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- `
    --maximum-parallel-tests 1 --output Detailed --no-ansi --report-trx --report-html --results-directory $runRoot 2>&1 | Tee-Object -FilePath $consoleLog
$testExitCode = $LASTEXITCODE
if ($testExitCode -ne 0) { throw "Full TUnit gate failed with exit code $testExitCode. Evidence: $runRoot" }

$trx = Get-ChildItem -LiteralPath $runRoot -Filter '*.trx' -File | Select-Object -First 1
if ($null -eq $trx) { throw "TRX was not produced: $runRoot" }
[xml]$trxXml = Get-Content -LiteralPath $trx.FullName -Raw
$counters = $trxXml.TestRun.ResultSummary.Counters
if ([int]$counters.total -lt 675 -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) { throw "Invalid full-suite counters: $($counters.OuterXml)" }
$nonPassed = @($trxXml.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -ne 'Passed' })
if ($nonPassed.Count -ne 0) { throw "TRX contains non-passing results: $($nonPassed.testName -join ', ')" }
$slow = @($trxXml.TestRun.Results.UnitTestResult | Where-Object { [TimeSpan]$_.duration -gt [TimeSpan]::FromMinutes(3) })
if ($slow.Count -ne 0) { throw "Tests longer than 180 seconds: $($slow.testName -join ', ')" }
if (Select-String -LiteralPath $consoleLog -Pattern 'Unobserved task exception' -SimpleMatch -Quiet) { throw "Unobserved task exception found: $consoleLog" }
```

Если конечный тест длится более 180 секунд, это отдельный diagnosis/fix before green claim: зафиксировать name, TRX duration и причинный evidence; не добавлять timeout и не снижать expected count.

### STORM evidence transitions
| Scenario | Baseline status update before repair | Final passing evidence required | Final artifact update |
| --- | --- | --- | --- |
| `SC-0005-002` | Append actual `TS-0033` failure command/result; `status = failing` | `StormFilterResetExecutableSpecTests` 1/1 plus full suite | Append fresh evidence, set `status = passing`, refresh `automation_status`; retain historical rows |
| `SC-0011-001` | Append actual `TS-0017`/`TS-0031` failure command/result; `status = failing` | Specified auth contract 1/1 and auth BDD 1/1 plus full suite | Append fresh evidence, set `status = passing`, refresh `automation_status`; retain historical rows |
| `SC-0011-002` | Append actual `TS-0019`/`TS-0020`/`TS-0032` failure command/result; `status = failing` | Live server 2/2 and CRUD BDD 1/1 plus full suite | Append fresh evidence, set `status = passing`, refresh `automation_status`; retain historical rows |
| `SC-0015-002` | Append actual `TS-0024`/`TS-0026` failure command/result; `status = failing` | Platform contract 3/3 and BDD 1/1 plus full suite | Append fresh evidence, set `status = passing`, refresh `automation_status`; retain historical rows |

Only `docs/product/storm.json`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/bdd-lint.md`, `docs/product/reports/coverage.md` and `docs/product/reports/traceability.md` are updated for these transitions. `/storm:bdd-sync` verifies Scenario -> Test -> Step Definition -> Code links; `/storm:bdd-lint` records known warnings separately from failure evidence.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1 | Exact full TUnit command above | Counters, every TRX outcome, duration and merged-console scan | C:\tmp run directory, TRX, HTML, console log | N/A |
| AC-2 | Four server targeted commands, exact counts 2/1/1/1 | Inspect mapper validation and summaries | Detailed TUnit output | N/A |
| AC-3 | Platform targeted commands, exact counts 3/1 | Inspect current source seams and summaries | Detailed TUnit output | N/A |
| AC-4 | Filter reset BDD, exact count 1 | Headless per-tab state values | Detailed TUnit output + full TRX | N/A |
| AC-5 | `MainWindowViewModelFixtureLifecycleTests`, exact count 3 | `rg` zero legacy calls, full console scan | Detailed TUnit output + full console | N/A |
| AC-6 | Validator, `/storm:bdd-sync`, `/storm:bdd-lint` | Scenario transition table and report diff | Five named product artifacts | N/A |

## 12. Риски и edge cases
- Changing string contracts can accidentally weaken semantic coverage. Mitigation: assert concrete current startup/auth responsibilities, not merely type names.
- Targeted TUnit discovery via `--list-tests` lists all tests in the current runner and can create false filter evidence. Mitigation: use only actual `dotnet test` execution summaries with the exact counts in the selection table.
- Autosave drain can deadlock the UI dispatcher or lose faults after a snapshot. Mitigation: dispose producers, same-lock admission seal, one outer drain, flattened fault aggregation and three focused async lifecycle tests.
- Migrating 155 cleanup calls can leave a synchronous teardown path. Mitigation: inventory before/after migration; zero `.CleanTasks()` matches is an AC and any non-async caller is converted instead of wrapped synchronously.
- Mapping ignore could hide a new server field. Mitigation: scope it exactly to domain-only `ExtensionData`; all mapped transport fields remain validated.
- Full serial run is long. Mitigation: no deadline, live output, persisted TRX/HTML/console evidence and a post-run 180s duration gate rather than a wall-clock timeout.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Не маскируйте ошибки тестами» | Contract assertions are being changed | Preserve behavior-level checks and show before/after failure evidence | mitigated |
| «Не меняйте продукт ради тестов» | Filter/reset and teardown touch UI/ViewModel code | No user-visible semantic change; focused UI regression remains required | mitigated |
| «Не ограничивайте долгий прогон timeout-ом» | Full suite already took 22m | Explicit no global timeout; streaming monitor remains | mitigated |
| «Не раздувайте STORM artifacts» | BDD evidence is involved | Update only evidence/status links; keep stories/AC/Gherkin wording | mitigated |

### Rework Prevention Checklist
- User-observable scenarios, decision ledger, acceptance-to-test matrix and expected objections are filled.
- No user-owned choice blocks EXEC.
- Every current failure class and the stderr race have a verification path.

## 13. План выполнения
1. Record the reproducible baseline failures in the four named STORM scenario records without deleting historical evidence.
2. Add precise `ExtensionData` ignores and run the four exact-count server mapping/live/BDD gates.
3. Update platform/auth contract checks to current observable seams; run the two exact-count platform gates.
4. Correct per-tab filter scenario setup/assertions; run the exact-count headless BDD gate.
5. Implement the specified async sealing/draining protocol, migrate all 155 cleanup calls, add three named lifecycle regressions and prove zero legacy call sites.
6. Run all targeted gates and update the four STORM scenario records to `passing` only after their fresh evidence is green; run `/storm:bdd-sync`, `/storm:bdd-lint` and validator, then update four named reports.
7. Run the exact full detailed sequential command with no deadline; enforce TRX counters, non-passing outcomes, 180s duration and `Unobserved task exception` checks.
8. Run post-EXEC review; commit only after user requests delivery commit.

## 14. Открытые вопросы
Нет блокирующих. Private lifecycle seam, call-site migration rule, expected regression count and STORM transition contract определены выше. Stop rule: если async migration любого call site потребует public API, product behavior или persisted-data change, остановиться и запросить отдельную QUEST-spec вместо sync wrapper.

## 15. Соответствие профилю
- Профиль: `storm-product-development` route `delivery-task` through QUEST; `dotnet-desktop-client`; `testing-dotnet`; local UI testing override.
- Выполненные требования профиля: existing BDD scenario chain is preserved; test/code changes wait for QUEST approval; UI-facing regression retains Avalonia.Headless coverage; product artifacts remain Russian; `Scenario -> Test -> Step Definition -> Code` links and scenario statuses sync only from actual runner evidence.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Server/AppModelMapping.cs` | Ignore `ExtensionData` inbound | Restore mapper validation |
| `src/Unlimotion.Test/PlatformShellProjectContracts.cs` | Current platform startup contract assertions | Remove obsolete API-name dependency |
| `src/Unlimotion.Test/ServerStorageAuthContract.cs` | Current auth persistence contract assertion | Align BDD evidence with refactored source |
| `src/Unlimotion.Test/FilterResetUiContract.cs` | Per-tab setup and assertions | Verify existing scoped filter behavior |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | Private admission seal and cached per-item drain task | Prevent background write admission after cleanup starts |
| `src/Unlimotion.Test/MainWindowViewModelFixture.cs` | `IAsyncDisposable`, cached cleanup task, outer fault aggregation and ordered cleanup | Observe save outcomes before delete |
| `src/Unlimotion.Test/MainWindowViewModelFixtureLifecycleTests.cs` | Three focused async lifecycle regressions | Prove drain, aggregation/idempotency and no UI deadlock |
| 28 existing files in `src/Unlimotion.Test` with 155 `.CleanTasks()` calls | Async teardown migration to `CleanTasksAsync` | Remove every synchronous fixture deletion path |
| `docs/product/storm.json`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/bdd-lint.md`, `docs/product/reports/coverage.md`, `docs/product/reports/traceability.md` | Exact evidence/status transitions only | Preserve STORM traceability and distinguish baseline failure from final proof |
| `specs/2026-07-18-full-suite-regression-recovery.md` | This approved scope and evidence | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| AutoMapper | `ExtensionData` unmapped and host fails | Domain-only property explicitly ignored inbound |
| Shell/auth contracts | Check stale source strings | Check current observable seams |
| Filter reset BDD | Always inspects All Tasks collection | Inspects the collection of the reset tab |
| Fixture cleanup | Delete while autosaves can complete later | Dispose producers, same-lock seal, one outer drain/aggregate, then delete; all 155 callers await cleanup |

## 18. Альтернативы и компромиссы
- Вариант: вернуть obsolete `TaskStorageFactory` API or old `RefreshToken` signature.
- Плюсы: old tests pass with fewer test edits.
- Минусы: reintroduces obsolete architecture and expands production risk.
- Почему выбранное решение лучше в контексте этой задачи: the source runtime refactor is intentional; tests must prove its current contract rather than pull production code backward.
- Вариант: вынести lifecycle fix в отдельную implementation spec и оставить текущий full-suite gate частичным.
- Плюсы: меньший immediate diff.
- Минусы: текущие 7 unobserved filesystem faults и full-suite gate остаются без исправления; outcome contract не достигается.
- Решение: lifecycle остаётся отдельным work package внутри этой large spec с точной boundary, inventory и rollback, потому что является необходимой причиной текущего gate failure.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | 11 required sections present; user scenarios, ledger, AC matrix, objections и role review заполнены |
| B. Качество дизайна | 6-10 | PASS | Lifecycle protocol, 155-call migration, STORM transitions и Non-Goals конкретизированы |
| C. Безопасность изменений | 11-13 | PASS | Нет schema/public API migration; async-only cleanup и stop rule определены |
| D. Проверяемость | 14-16 | PASS | Exact filters/counts, TRX parser, duration/error scan и named reports определены |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет user-owned design choice; нужен только QUEST approval before EXEC |
| F. Соответствие профилю | 20 | PASS | QUEST/STORM/Avalonia.Headless/Russian artifact requirements recorded |

Структурная самопроверка: `H2=22`, PowerShell blocks `2/2` parse без ошибок, mandatory sections `11/11`, tabs/trailing whitespace `0`.

Итог: ГОТОВО К ПОДТВЕРЖДЕНИЮ.

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Full-suite outcome, separate lifecycle package, exclusions и stop rules измеримы |
| 2. Понимание текущего состояния | 5 | 9 failures, 7 stderr faults, 155 legacy calls и stale STORM evidence классифицированы |
| 3. Конкретность целевого дизайна | 5 | Same-lock seal, outer drain, exact target counts, named files/tests/reports определены |
| 4. Безопасность | 5 | Нет behavior/data migration; idempotency, fault aggregation и rollback заданы |
| 5. Тестируемость | 5 | Targeted, lifecycle, artifact и machine-checked full-run gates mapped |
| 6. Готовность к автономной реализации | 5 | Нет открытого user-owned решения; EXEC остановится на public/product/data change |

Итоговый балл: 30 / 30.
Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Сохраняются ли semantics server storage и per-tab reset? | PASS | Existing behavior-level assertions retained |
| UX / designer | applicable | Соответствует ли reset per-tab user expectation без нового layout/flow? | PASS | Visual artifact не применим; existing Avalonia.Headless state evidence retained |
| Tester / validation | applicable | Имеет ли каждый failure/lifecycle/STORM state проверяемое evidence? | PASS | Exact counts, named regressions, TRX and console gates defined |
| Developer / architect | applicable | Coherent ли mapping и lifecycle boundaries? | PASS | `ExtensionData` narrow; same-lock seal and aggregation explicit |
| Delivery / operations / security | applicable | Addressed ли long-run, temp evidence и auth/runtime risks? | PASS | No timeout, `C:\tmp` evidence, no public API rollback |

### Post-SPEC Review
- Статус: PASS.
- Scope reviewed: эта spec; central `routing-matrix`, `quest-governance`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`, `storm-product-development`, `testing-dotnet` и local UI override; full-run TRX/HTML/stdout/stderr; current `TaskItemViewModel`, `MainWindowViewModelFixture`, `AppModelMapping`, seven target test classes, `storm.json` и four STORM reports.
- Decision: можно повторно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: PASS. Only the full-suite recovery outcome, its four product scenario contracts, lifecycle reliability and their named STORM evidence are in scope.
  - Contract pass: PASS. Non-Goals prohibit API, data, UX and Gherkin-semantic changes; every planned test change has an observable responsibility and an exact evidence path.
  - Adversarial risk pass: PASS after correction. `--list-tests` was shown to output all discovered tests in this runner and is excluded as selection evidence. Actual `dotnet test ... ServerStorageLiveIntegrationTests` ran exactly 2 selected tests; the revised commands require actual summary counts for every gate.
  - Role-Based pass: PASS. The role table covers product semantics, UI reset state, validation, architecture and delivery risks.
  - Fix and re-review: PASS. Re-read lifecycle order, selection table, STORM transitions, full-run PowerShell parser and file allowlist after their amendments.
  - Stop decision: PASS. Explicit QUEST approval remains the only required handoff; public/product/data change remains an automatic stop.
- Evidence inspected: full run TRX/HTML/stdout/stderr; actual server live targeted run (2 total, 2 expected baseline mapping failures); current source lifecycle/mapping contracts; `storm.json` scenario links; TUnit lifecycle/filter documentation; PowerShell parse, mandatory-section, whitespace and STORM validator checks.
- Depth checklist: unrelated changes excluded; ACs measurable; user scenarios/decision ledger/objections complete; exact target count and full-run evidence are concrete; lifecycle call-site migration is bounded; STORM history is retained; no hidden API or UX contract change planned; known warning policy is explicit.
- No unresolved approval-blocking spec gaps. Runtime tests remain RED before EXEC by design and are not represented as passing evidence.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | validation | Previous discovery-based filter proof was invalid | Use actual `dotnet test` execution, exact counts and no `--list-tests` evidence | fixed in SPEC |
| HIGH | teardown | Lifecycle design omitted producer barrier, call-site migration and named regressions | Same-lock seal, outer drain, async migration 155 calls and three tests | fixed in SPEC |
| HIGH | STORM evidence | Passing states were historical and no transition contract existed | Four scenario transition table and five named artifacts | fixed in SPEC |
| MEDIUM | full-run gate | Command/evaluation were not fail-closed | Exact no-timeout command, TRX/console/duration checks | fixed in SPEC |
| MEDIUM | tests/contracts | BDD contracts assert obsolete API names | Update to current behavior-level seams | planned for EXEC |
| HIGH | mapping | Live server host cannot validate mappings | Ignore domain-only extension data | planned for EXEC |

- Fixed before continuing: selection evidence, lifecycle design, STORM transition design, full-run validation contract and scope scale.
- Checks rerun: actual server live targeted command selected 2/2 and failed only on known mapper baseline; full serial suite baseline is 675 total/9 failed; PowerShell blocks parse 2/2; mandatory sections 11/11; no whitespace; `validate-artifacts.py` => 0 errors, 18 known duplicate-step warnings; `git diff --check` clean for tracked files.
- Needs human: explicit QUEST approval.
- Residual risks / follow-ups: no performance remediation is planned because baseline has no test over 3 minutes; implementation can still expose an async migration blocker, which triggers the explicit stop rule.

### Post-EXEC Review
- Статус: PASS.
- Реализация: входные mappings явно игнорируют доменный `TaskItem.ExtensionData`; auth/platform/filter contracts синхронизированы с текущими production seams; fixture получила same-lock seal, единый drain с агрегацией fault и идемпотентный async cleanup; 155 legacy-вызовов переведены на `CleanTasksAsync`.
- Регрессионное покрытие: добавлены 3 lifecycle-теста; три гонки ожидания, обнаруженные первым полным прогоном, устранены только в test synchronization без изменения product behavior.
- Targeted evidence: lifecycle 3/3; auth contract 1/1 и BDD 1/1; server live integration 2/2 и BDD 1/1; platform contracts 3/3 и BDD 1/1; filter reset BDD 1/1; дополнительные relation/wanted/delete regression gates прошли.
- Full-suite evidence: `678/678` за `32m20.317s`, failed `0`, skipped `0`, unobserved markers `0`; максимум `62.047s`, тестов дольше `180s` нет. TRX/HTML/console сохранены в `C:\tmp\unlimotion-full-green-20260718-190543-4132496745d74045a572c9895b1cf742`.
- STORM evidence: четыре сценария возвращены в `passing`, исторические RED-записи сохранены; validator завершился с `0 errors`, `18` известными warnings, executable ratio `45/45`, step reuse `181/181`.
- Ограничения соблюдены: `.feature`, acceptance criteria, automation IDs, test annotations, public API, data format и UX/layout не менялись.
- Residual risk: полный serial-прогон занимает около 32 минут и кратковременно использовал до ~3.5 GB working set, но процесс оставался responsive, прогресс наблюдался, а отдельные тесты не приближались к лимиту 3 минут.

### Post-Rebase Validation
- Ветка перебазирована на `origin/main@75efc04`; конфликты lifecycle fixture разрешены в пользу более полного upstream-контракта из PR #275, Telegram callback/status routing согласован с общим policy из PR #277.
- После прохождения CI выполнен финальный rebase на `origin/main@ad90260`; новый PR #278 изменил только две SPEC-документации, а двухточечный diff между проверенным и финальным head для `src`, `tests` и `.github` пуст.
- Post-rebase integration fixes ограничены тестовым слоем: ручные BDD fixture-владельцы используют `CleanupFixtureAsync`; wanted regression работает с авторитетным repository VM; completion-block BDD проверяет `TaskStatusOption.IsEnabled`.
- Targeted evidence: lifecycle 4/4; Telegram status 11/11; callback coverage 7/7 и BDD 1/1; tree-search 7/7; filter reset 1/1; wanted 2/2; relation add 1/1; multi-parent delete 1/1; шесть затронутых executable BDD 6/6.
- Full-suite evidence: `Unlimotion.Test` 830/830 за 19m35.329s; `Unlimotion.UiTests.Headless` 33/33 за 1m34.053s после свежего restore; failed 0, skipped 0.
- TUnit HTML duration audit: максимум отдельного теста 35.837s в основном наборе и 3.502s в Headless; тестов дольше 180s нет.
- Исторические 678/678 и RED/timeout evidence сохранены; Gherkin, acceptance criteria, test annotations, production behavior и UX/layout не менялись.

## Approval
Подтверждено пользователем `18.07.2026`: "Спеку подтверждаю". Статус: `APPROVED FOR EXEC`.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Full-suite diagnostic and recovery scope | 0.96 | Нет блокирующих данных | Запросить approval | Да | Нет, ожидается `Спеку подтверждаю` | Four failure mechanisms are cohesive under one full verification outcome; no product semantics change is proposed | Этот spec; full-run artifacts in `C:\tmp` |
| SPEC | Review findings: filter proof, lifecycle scope, STORM evidence and full-run gate | 0.99 | Точный TUnit selection contract | Проверить actual targeted command и обновить spec | Нет | Пользователь запросил «Исправь» | `--list-tests` оказался невалидным evidence; спецификация требует фактических execution summaries | Этот spec; `TaskItemViewModel`, fixture, target tests, STORM artifacts |
| SPEC | Исправление и повторный post-SPEC review | 0.99 | Нет | Запросить approval | Да | Нет, ожидается `Спеку подтверждаю` | Внесены exact counts, lifecycle protocol, state transitions и fail-closed full gate; structural checks прошли | Этот spec |
| EXEC | Переход по явному QUEST approval | 1.00 | Нет | Снять exact RED evidence и начать implementation | Нет | Да, пользователь подтвердил: `Спеку подтверждаю` | Approval gate выполнен; реализация ограничена утверждёнными mapping/test/lifecycle/STORM contracts | Этот spec; planned changed files из раздела 16 |
| EXEC | Mapping/contracts и fixture lifecycle | 0.99 | Нет | Выполнить targeted и полный serial gates | Нет | Не требовалось | Реализованы утверждённые narrow fixes; cleanup migration завершена для 155 вызовов | Mapping, auth/platform/filter contracts, fixture, `TaskItemViewModel`, lifecycle tests и async callers |
| EXEC | Первый полный serial-прогон | 0.98 | Причина трёх новых test-only failures | Изолировать и устранить synchronization races | Нет | Не требовалось | 675/678 подтвердили устранение исходных девяти failures; три остаточных сбоя локализованы в ожидании async command/save/delete | `TestHelpers.cs`, `MainWindowViewModelTests.cs`; evidence в `C:\tmp\unlimotion-full-20260718-182737-45a648b22e5b4724aa3bc845b1a3b651` |
| EXEC | Финальная валидация и BDD sync | 1.00 | Нет | Выполнить post-EXEC review | Нет | Не требовалось | Полный набор прошёл 678/678, validator 0 errors; фактические статусы и reports синхронизированы без изменения Gherkin/annotations | `storm.json`, четыре STORM reports, TRX/HTML/console evidence |
