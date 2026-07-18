# Асинхронный lifecycle тестовой fixture

## 0. Метаданные
- Тип (профиль): CI / .NET test infrastructure / TUnit lifecycle.
- Владелец: Unlimotion maintainers.
- Масштаб: medium — одна корневая гонка, минимальный internal producer barrier в ViewModel и 147 consumer call sites в 21 test file.
- Целевое семейство / behavior baseline: Не применимо — задача не меняет model/prompt behavior.
- Поверхность: Codex implementation, Microsoft.Testing.Platform/TUnit local runner и GitHub Actions `All tests`.
- Effective runtime: `global.json` minimum .NET SDK `10.0.100` с `rollForward=latestFeature`; фактически локально и в проверенном CI run resolved `10.0.302`; `net10.0`; TUnit `1.44.0`; Microsoft.Testing.Platform; `windows-latest`; `--maximum-parallel-tests 1`.
- Eval baseline / evidence:
  - PR #274 run `29584922060`, attempt 1, job `87899423421`: 600 total, 599 succeeded, 1 отдельный UI failure, затем три unobserved `FileNotFoundException` из удалённых `MainWindowViewModelFixture_*`; длительность 6m26s;
  - тот же run, attempt 2, job `87902291442`: test assembly стартовала, assertion output отсутствовал, job отменён по 30-minute timeout; это согласуется с lifecycle race, но самостоятельно не доказывает ту же причину;
  - в текущей рабочей сессии локальный full-suite прогон на том же head вошёл в low-CPU/no-output hang; discovery, одиночный тест и класс отдельного UI failure завершались; durable log этого локального наблюдения не сохранён, поэтому оно не используется как самостоятельное доказательство root cause;
  - source inventory: 147 вызовов `CleanTasks()` в 21 файле.
- Целевой релиз / ветка: base `origin/main@5aebebcb34eabe35fcdb7a47ff76ffdc2a7e16dd`; branch `fix/test-fixture-lifecycle`; отдельный PR в `main` до повторного CI/merge PR #274.
- Ограничения:
  - до отдельной точной approval-фразы изменялась только эта spec; gate пройден 2026-07-18;
  - EXEC меняет `src/Unlimotion.Test`, `src/Unlimotion.ViewModel/TaskItemViewModel.cs` и новый `src/Unlimotion.ViewModel/AssemblyInfo.cs`; другие paths запрещены без повторной review/approval;
  - production storage/model/workflow semantics не меняются; единственное production-assembly изменение — internal save-shutdown seam в `TaskItemViewModel` и friend-assembly declaration для тестов;
  - child-spec является sequencing prerequisite, а не Stage 2 implementation.
- Связанные ссылки:
  - master roadmap: `specs/2026-07-17-readme-reliability-roadmap.md` в PR #274;
  - PR #274: <https://github.com/Kibnet/Unlimotion/pull/274>;
  - failing/timed-out run: <https://github.com/Kibnet/Unlimotion/actions/runs/29584922060>;
  - attempt-1 job: <https://github.com/Kibnet/Unlimotion/actions/runs/29584922060/job/87899423421>;
  - attempt-2 job: <https://github.com/Kibnet/Unlimotion/actions/runs/29584922060/job/87902291442>.

## 1. Overview / Цель
Сделать владельцем завершения фоновых test-fixture writes один awaitable cleanup contract: атомарно запретить регистрацию новых saves, дождаться уже зарегистрированных, затем ровно один раз dispose/delete и явно сообщить об ошибках. Это устраняет наблюдаемую гонку `delayed save/enumeration -> fixture directory deletion`, не меняя обычное production behavior.

Outcome contract:
- Success means:
  - детерминированный lifecycle regression сначала RED на старом порядке и GREEN после исправления;
  - два конкурентных/повторных cleanup caller ждут одну и ту же task;
  - каталог и task file существуют, пока controlled save заблокирован, и удалены после его release;
  - старых синхронных `CleanTasks()` call sites нет;
  - targeted test проходит 20 последовательных запусков;
  - full `Unlimotion.Test` и `Unlimotion.UiTests.Headless` проходят serially без `Unobserved task exception` и без timeout;
  - отдельный PR green/ready/merged; затем PR #274 обновлён от `main`, повторно green/ready/merged.
- Итоговый артефакт / output: узкий lifecycle diff (`TaskItemViewModel` internal barrier + test fixture/consumers), deterministic regressions, local validation logs, green GitHub Actions evidence и PR body с root-cause/rollback.
- Stop rules:
  - approval `Спеку подтверждаю` получен 2026-07-18; EXEC разрешён в утверждённом scope;
  - STOP и новая design review, если требуется production change шире утверждённого internal save-shutdown seam;
  - STOP и отдельная диагностика, если post-fix full suite сохраняет hang/unobserved exceptions при GREEN targeted regression;
  - unrelated `Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter` failure не исправляется в этом package и не выдаётся за lifecycle evidence.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion.Test/MainWindowViewModelFixture.cs:143-167` реализует синхронный `CleanTasks()`:
  1. выставляет `isCleaned`;
  2. синхронно вызывает `MainWindowViewModel.Dispose()` и storage/config dispose;
  3. сразу удаляет config, expansion-state, tasks directory и fixture directory.
- `MainWindowViewModelFixture.Try(Action)` делает три попытки с `Thread.Sleep(100)`, а последнюю ошибку молча теряет. Поэтому leaked directory или delete race могут выглядеть как успешный cleanup.
- `TaskItemViewModel.ExecuteSaveCommand()` запускает `SaveItemCommand.Execute().ToTask()` fire-and-forget, но сохраняет task в `_pendingSaves`; `WaitForPendingSavesAsync()` уже является API для ожидания snapshot.
- Save producer не имеют shutdown barrier:
  - common property autosave проходит через `.Throttle(PropertyChangedThrottleTimeSpanDefault)`;
  - repeater autosave проходит через `.Throttle(TimeSpan.FromSeconds(2))`;
  - predicate `IsInitialized` вычисляется до throttle, поэтому смена provider после постановки события не отменяет отложенный callback.
- Status save проходит через `TaskTreeManager.CanTransitionToStatus()`, который перечисляет весь graph через `Storage.GetAll()`.
- `FileTaskStorage.GetAll()` читает каждый file внутри `Task.Run`; удаление fixture directory во время незавершённого enumeration/read даёт `FileNotFoundException`.
- Initial load не является обнаруженным владельцем утечки:
  - `MainWindowViewModel.Connect()` awaits `taskStorage.Init()`;
  - `UnifiedTaskStorage.Init()` awaits `BuildInitialTaskViewsAsync()` и cache fill.
- `.github/workflows/tests.yml` уже выполняет `Unlimotion.Test` и Headless serially с `--maximum-parallel-tests 1`; повышение timeout или уменьшение parallelism не исправляет отсутствующий await.
- Синхронный cleanup используется 147 раз в 21 test file. `BaseModelTests` реализует `IDisposable`; два call sites в `LocalizationDisplayDefinitionTests` предварительно dispose ViewModel вручную и затем вызывают fixture cleanup.
- TUnit `1.44.0` штатно вызывает `IAsyncDisposable.DisposeAsync()` и распространяет cleanup exception; локальные package XML/API подтверждают async disposer contract.

CI evidence interpretation:
- Attempt 1 напрямую подтверждает минимум три чтения из уже удалённых fixture directories.
- Единственный assertion failure в attempt 1 относится к responsive emoji toolbar и не объясняет три lifecycle exceptions.
- Attempt 2 подтверждает 30-minute no-result timeout после загрузки assembly. Он повышает приоритет lifecycle fix, но без стека не считается независимым доказательством одной и той же причины.

## 3. Проблема
Test fixture удаляет принадлежащие ей файлы до завершения фоновой работы, которая всё ещё использует эти файлы, а затем скрывает delete failures. Из-за отсутствия единого awaitable owner cleanup становится вероятностным: одиночные тесты проходят, а полный suite может оставить unobserved exceptions или зависнуть на finalization/teardown.

## 4. Цели дизайна
- Один async owner для drain, dispose и delete.
- Детерминированная идемпотентность: все caller получают одну cleanup task и один результат/exception.
- Atomic producer seal под тем же lock, который допускает и регистрирует save.
- Drain единственного snapshot всех уже зарегистрированных pending saves до storage dispose/delete, включая completed/faulted tasks.
- Явное распространение save/dispose/delete failures вместо ложного success.
- Полная миграция test consumers на awaitable cleanup.
- Сохранение normal production behavior, persistence schema, UI и CI topology; internal seam вызывается только fixture.

## 5. Non-Goals (чего НЕ делаем)
- Не менять `MainWindowViewModel`, `UnifiedTaskStorage`, `TaskTreeManager`, `FileTaskStorage` или `TaskSourceManager`.
- Не менять normal autosave behavior `TaskItemViewModel`; разрешены только `_acceptingSaves` under-lock gate и internal seal API, вызываемый тестовой fixture.
- Не добавлять cancellation pending save: cleanup после старта должен завершить write или явно упасть.
- Не подавлять `TaskScheduler.UnobservedTaskException`.
- Не ловить глобально `FileNotFoundException` и не ослаблять storage validation.
- Не увеличивать GitHub Actions timeout и не менять test parallelism.
- Не исправлять `Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter` или любой другой unrelated assertion.
- Не решать потенциальный production source-switch drain. Это отдельный MEDIUM follow-up без доказанной пользовательской потери данных.
- Не менять README, master roadmap или Stage 2 contract в этом PR.
- Не создавать UI video: production UI и user flow не меняются.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- `MainWindowViewModelFixture`:
  - реализует `IAsyncDisposable`;
  - выдаёт один `Task CleanTasksAsync()` на весь lifecycle;
  - закрывает VM-side producers, atomically seals task saves и ждёт returned snapshots до storage dispose/delete;
  - выполняет bounded retry удаления и не скрывает terminal failure.
- `BaseModelTests`:
  - реализует `IAsyncDisposable` вместо `IDisposable`;
  - awaits fixture cleanup через TUnit async lifecycle.
- Каждый direct fixture owner:
  - awaits `CleanTasksAsync()` в своём `finally`;
  - для Headless сохраняет cleanup внутри dispatcher scope и завершает его до session dispose.
- `MainWindowViewModelFixtureLifecycleTests`:
  - владеет controlled-save regression и idempotency assertions.
- `TaskItemViewModel`:
  - под `_pendingSavesLock` атомарно проверяет `_acceptingSaves`, запускает и регистрирует save;
  - internal seal под тем же lock запрещает новые saves и возвращает один snapshot всех зарегистрированных tasks.
- `Unlimotion.ViewModel/AssemblyInfo.cs`:
  - открывает internal lifecycle seam только assembly `Unlimotion.Test`.
- GitHub Actions:
  - остаётся без изменений и выступает delivery verifier.

### 6.2 Детальный дизайн

#### Async ownership и идемпотентность
- У fixture появляется private lock и nullable `Task cleanupTask`.
- `CleanTasksAsync()` не является `async` wrapper: под lock он один раз создаёт `CleanTasksCoreAsync()` и возвращает тот же object всем caller.
- `DisposeAsync()` возвращает/awaits тот же cleanup operation.
- Повторный caller после success получает completed task; после failure — ту же faulted task. Cleanup не запускается повторно и не превращает failure в success.
- Старые `isCleaned` и public synchronous `CleanTasks()` удаляются после полной consumer migration; blocking `.GetAwaiter().GetResult()` adapter не сохраняется.

#### Producer barrier и drain contract
- `TaskItemViewModel` получает private `_acceptingSaves = true` под существующим `_pendingSavesLock`.
- `ExecuteSaveCommand()` под этим lock:
  1. возвращается без save, если `_acceptingSaves == false`;
  2. иначе создаёт `SaveItemCommand.Execute().ToTask()` и добавляет task в `_pendingSaves` до release lock;
  3. после lock запускает existing completion observer.
- Internal `SealPendingSaves()` под тем же lock ставит `_acceptingSaves = false`, один раз создаёт и кэширует `Task.WhenAll(_pendingSaves.ToArray())` либо `Task.CompletedTask`, а повторно возвращает тот же sealed snapshot task.
- Операции линейризуемы относительно одного lock: save либо зарегистрирован до seal и входит в returned snapshot, либо приходит после seal и становится no-op. Вероятностные `Yield`/quiet-window/sleep не используются.
- Cleanup получает repository reference и синхронно вызывает `MainWindowViewModelTest.Dispose()`, закрывая VM-side producers, но оставляя task/storage живыми для завершения writes.
- Затем снимается snapshot `taskRepository.Tasks.Items`; для каждого task синхронно вызывается `SealPendingSaves()`, из всех returned tasks ровно один раз создаётся outer `drainTask = Task.WhenAll(sealedSnapshots)`, и он awaits независимо от `IsCompleted`/`IsFaulted`.
- После seal новые delayed property/repeater callbacks не могут зарегистрировать save; quiescence loop больше не нужен.
- Cleanup API не принимает cancellation token. Внешний test timeout применяется только к regression await и не отменяет owned save.
- Если captured save task faulted, cleanup всё равно пытается освободить resources/delete artifacts, затем возвращает fault. После await outer `drainTask` ошибки берутся из `drainTask.Exception!.Flatten().InnerExceptions`, а не только из exception, выброшенного `await`; outer task инспектируется ровно один раз, поэтому faults из вложенных `Task.WhenAll` не теряются и не дублируются.

#### Dispose/delete contract
- Полный порядок остаётся единым:
  1. capture repository reference;
  2. `MainWindowViewModelTest.Dispose()`;
  3. snapshot task view models;
  4. synchronously seal every task and capture returned pending tasks;
  5. await every captured task, including already completed/faulted;
  6. storage `IDisposable.Dispose()`;
  7. configuration dispose;
  8. config file delete;
  9. expansion-state file delete;
  10. tasks directory delete;
  11. fixture directory delete.
- Два ручных предварительных ViewModel dispose в `LocalizationDisplayDefinitionTests` удаляются: fixture — единственный владелец порядка.
- Delete helper выполняет не более трёх попыток с async backoff; отсутствие path считается success.
- После последней неудачи helper выбрасывает exception с operation/path и сохраняет inner exception.
- Если drain/dispose/delete дали несколько ошибок, cleanup возвращает `AggregateException`, чтобы исходная lifecycle ошибка не была заменена последней delete error.

#### Consumer migration
- Все 147 `fixture.CleanTasks()`/`projectionFixture.CleanTasks()` call sites заменяются на awaited `CleanTasksAsync()`.
- Существующие async test methods/lambdas сохраняются async; synchronous consumer при необходимости становится `async Task`.
- `BaseModelTests.Dispose()` заменяется на `ValueTask DisposeAsync()`.
- `RunWithTreeProjectionAsync` awaits `projectionFixture.CleanTasksAsync()` внутри `session.DispatchAsync`, затем outer finally awaits headless session dispose.
- Nullable fixture cleanup использует явный null-check; null-conditional await не вводится.
- Для fixture без `Connect()` `taskRepository == null`: cleanup пропускает snapshot/seal/drain/storage-dispose, но ровно один раз dispose MainWindow/config и удаляет temp paths.
- В четырёх callers, где после cleanup обязательно восстанавливается global state (две localization, application font resources, requested theme), restore помещается во вложенный `finally`, чтобы cleanup fault его не пропустил.

#### Deterministic regression
Новый тест `MainWindowViewModelFixtureLifecycleTests.CleanTasksAsync_ConcurrentCallersWaitForInFlightSaveBeforeDirectoryDeletion`:
1. Создаёт fixture и awaits `MainWindowViewModelTest.Connect()`.
2. Выбирает существующий task и заменяет public `SaveItemCommand` контролируемой command с `started`/`release` `TaskCompletionSource`, созданными с `RunContinuationsAsynchronously`; сразу подписывает `ThrownExceptions` наблюдающим test observer, чтобы expected fault не ушёл в ReactiveUI default exception handler.
3. Меняет `Status` на другое значение, чтобы existing `ExecuteSaveCommand()` зарегистрировал task в `_pendingSaves`.
4. Awaits `started`.
5. Вызывает `CleanTasksAsync()` дважды и `DisposeAsync().AsTask()` один раз; fixture disposes MainWindow, seals task saves и все три caller ждут одну core operation.
6. Пока первая command заблокирована, повторно меняет status и проверяет, что invocation count остаётся `1`: producer barrier не допускает save после seal.
7. До `release` не бросает assertion: записывает `sameCleanTask`, `disposeJoinedSameOperation`, `completedBeforeRelease`, existence files/directories и invocation count. Это нужно, чтобы old-order command fault после release не заменил ожидаемый RED marker.
8. Освобождает `release`; каждый реально начат controlled invocation затем вызывает `repository.Update(task)`, то есть проходит production `TaskTreeManager`/`Storage.GetAll` path.
9. Bounded-observes все cleanup references и все command execution tasks, сохраняя exception как данные; затем первой проверкой делает `completedBeforeRelease == false` с точным message `cleanup completed before controlled save release`. После неё проверяет same-operation, invocation count, отсутствие command fault и удаление directories.
10. `finally` всегда выполняет `release.TrySetResult()`, bounded-observe без throw всех уже созданных cleanup/command tasks, затем dispose `ThrownExceptions` subscription и controlled command. Сам regression не оставляет teardown/background work и не маскирует primary test failure даже при раннем setup/timeout failure.

Для RED gate сначала добавляется тест и минимальный compile seam, который делегирует текущему synchronous cleanup. Ожидаемый RED: cleanup завершается/удаляет directory до `release`. После этого реализуется async owner contract.

#### Visual planning и UI evidence
- Visual planning artifact: Не применимо — production layout, copy и interaction не меняются.
- UI test video evidence: Не применимо — это test-infrastructure teardown fix; evidence дают deterministic lifecycle test, Headless suite и CI logs.
- Headless suite всё равно обязателен, потому что consumer inventory включает Avalonia Headless flows и меняется их teardown ordering.

#### Performance
- Normal cleanup добавляет lock-only seal и ожидание только реально зарегистрированных saves; delayed callbacks после seal становятся no-op.
- Нельзя добавлять unconditional multi-second sleeps.
- Таргетированный test stress должен завершаться в bounded time; full suite должен укладываться в существующий 30-minute CI timeout без его изменения.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Maintainer запускает один lifecycle regression | Targeted TUnit command | Test reliably GREEN after fix and RED on old ordering | RED/GREEN logs | TF-AC-01,02 |
| Maintainer запускает full test workflow | Local/PR serial suites | Suite completes; no fixture `FileNotFoundException`, unobserved task or timeout | full logs + GitHub check | TF-AC-08,10 |
| Два teardown path вызывают cleanup | Concurrent/repeated calls | Оба await same completion/failure; delete executes once | regression assertions | TF-AC-01,03 |
| Unrelated emoji UI assertion снова flakes | Full suite | Failure остаётся видимой и классифицируется отдельно; lifecycle code не расширяется | targeted rerun + PR note | TF-AC-09,10 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| No cleanup started, no pending saves | First `CleanTasksAsync` | dispose producers -> atomic seal -> dispose storage/config -> delete -> completed | Empty repository allowed | No blocking adapter |
| Fixture created, `Connect()` не вызван | First cleanup | skip task seal/drain, dispose config/VM, delete paths | `taskRepository == null` | Dedicated regression |
| No cleanup started, pending save active | First cleanup | waits; files stay present | Save failure recorded, cleanup still releases resources | Core regression |
| Cleanup running | Second cleanup | returns same task | No second dispose/delete | Reference equality asserted |
| Cleanup completed | Later cleanup | returns same completed task | No-op observable as same result | Idempotent |
| Cleanup faulted | Later cleanup | returns same faulted task | Failure not swallowed/retried | Diagnostic stability |
| Delete transiently locked | Delete helper | bounded retry then success | Final failure includes path | No `Thread.Sleep` |
| Headless dispatcher active | Inner finally | awaited cleanup inside dispatcher | Session dispose only afterwards | Preserves affinity/order |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Scope | agent | test fixture/consumers + internal `TaskItemViewModel` producer barrier/friend declaration | 0.99 | Без same-lock seal delayed throttle сохраняет гонку; более широкий runtime scope неоправдан | Нет |
| Cleanup API | agent | `Task CleanTasksAsync` + `IAsyncDisposable` | 0.99 | Blocking cleanup can deadlock and preserve race | Нет |
| Idempotency | agent | one shared cleanup task, including shared fault | 0.99 | Duplicate dispose/delete introduces new races | Нет |
| Pending saves | agent | same-lock atomic seal + one full snapshot, no cancellation | 0.99 | Yield/quiet-window не закрывают delayed producer; cancellation может потерять write | Нет |
| Delete failures | agent | bounded async retry, then propagate/aggregate | 0.98 | Silent leak recreates false success | Нет |
| UI flake | agent | explicitly separate package | 0.99 | Scope creep and unverifiable root-cause claim | Нет |
| Production source switching | agent | MEDIUM follow-up, not this EXEC | 0.90 | Similar risk is possible but not proven | Нет |
| Approval | user | exact new child-spec approval required | 1.00 | Roadmap governance would be bypassed | Да, самой фразой approval |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Test lifecycle | `MainWindowViewModelFixture.CleanTasks()` | async shared cleanup task | all test callers migrated atomically | build + zero old calls |
| TUnit instance cleanup | `BaseModelTests : IDisposable` | `IAsyncDisposable` | TUnit 1.44 supported | base-class tests + full suite |
| Save admission | `TaskItemViewModel.ExecuteSaveCommand()` + `_pendingSavesLock` | internal same-lock `_acceptingSaves`/`SealPendingSaves()` | normal runtime flag stays true; seam доступен только friend test assembly | producer-barrier regression |
| Pending saves | `TaskItemViewModel.WaitForPendingSavesAsync()` | existing API unchanged; fixture uses sealed one-time snapshot | no data/schema change | controlled real-storage regression |
| File cleanup | silent `Try(Action)` | bounded async retry + surfaced path | test temp paths only | mandatory injected delete-failure tests + full suite |
| CI | `.github/workflows/tests.yml` | unchanged | existing 30m/serial contract retained | PR checks |
| Persisted data | fixture temp files | no schema/content change | none | diff inspection |

## 7. Бизнес-правила / Алгоритмы
Lifecycle invariants:
1. `sealLinearized => no later ExecuteSaveCommand can register a save`.
2. `deleteStarted => every save registered before seal completed`.
3. Пока controlled save pending, `tasksDirectoryExists == true`.
4. `CleanTasksAsync()` создаёт не более одной core operation.
5. Повторные caller наблюдают одинаковый completion или одинаковый failure.
6. Cleanup failure никогда не становится success из-за retry exhaustion/swallow.
7. Save cancellation не используется как способ ускорить teardown.
8. Normal production autosave и workflow settings не меняются; internal seal вызывается только test fixture.

Atomic seal algorithm:
1. Capture repository reference and dispose MainWindow-side subscriptions.
2. If repository is null, skip task/storage branch and continue resource deletion.
3. Snapshot task view models.
4. Under each task's `_pendingSavesLock`, set `_acceptingSaves = false` and capture `Task.WhenAll` over all current saves, including completed/faulted.
5. Create one outer `drainTask = Task.WhenAll(sealedSnapshots)`, await it once, and on fault retain every `drainTask.Exception!.Flatten().InnerExceptions` entry once.
6. Dispose storage/config and delete paths; retain cleanup failures.
7. Throw the single retained error or one `AggregateException` without duplicate entries.

## 8. Точки интеграции и триггеры
- TUnit test-class teardown вызывает `BaseModelTests.DisposeAsync()`.
- Direct test `finally` blocks вызывают `await fixture.CleanTasksAsync()`.
- Headless dispatcher lambdas выполняют awaited cleanup до `SafeHeadlessUnitTestSession.DisposeAsync()`.
- `TaskItemViewModel.ExecuteSaveCommand()` и internal `SealPendingSaves()` используют один `_pendingSavesLock` как admission/join barrier.
- Controlled status mutation вызывает existing `TaskItemViewModel.ExecuteSaveCommand()`; test не обращается к `_pendingSaves` напрямую.
- GitHub pull request запускает unchanged `.github/workflows/tests.yml`.
- После merge fix PR ветка PR #274 обновляется от `main`, что повторно запускает required checks.

## 9. Изменения модели данных / состояния
- Production model/data/schema и normal runtime output: не меняются.
- Internal ViewModel lifecycle state:
  - добавляется `_acceptingSaves`, изначально `true`;
  - `SealPendingSaves()` необратимо переводит конкретный task VM в test-teardown state;
  - seam не сериализуется и не вызывается normal runtime path.
- Test fixture state:
  - удаляется `bool isCleaned`;
  - добавляются cleanup lock и `Task? cleanupTask`;
  - при необходимости добавляется test-visible read-only fixture directory path для assertions.
- Persisted fixture files остаются прежними и удаляются только после drain.

## 10. Миграция / Rollout / Rollback
- Миграция выполняется атомарно в одном lifecycle PR: internal seal, async fixture API и все 147 callers меняются вместе.
- Нет runtime/user data migration.
- Rollout:
  1. deterministic RED/GREEN locally;
  2. 20x targeted stress;
  3. full local unit + Headless suites;
  4. draft PR, green required checks, ready/merge;
  5. rebase/update PR #274, green checks, ready/merge.
- Rollback: revert lifecycle PR. Это вернёт прежнюю CI race, но не затронет production data/schema; normal runtime не требует migration.
- Нельзя откатывать частично только consumer awaits или только fixture API.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria
- **TF-AC-01:** `CleanTasksAsync()`/`DisposeAsync()` используют одну shared core task; concurrent mixed `CleanTasksAsync()` + `DisposeAsync().AsTask()`, repeated-after-success и repeated-after-failure caller не запускают второй dispose/delete и получают тот же completion/fault; две прямые `CleanTasksAsync()` references и `DisposeAsync().AsTask()` reference равны.
- **TF-AC-02:** Same-lock seal линейризуется с `ExecuteSaveCommand`: controlled in-flight status save остаётся pending; post-seal status mutation не запускает вторую command; до release files существуют; после release controlled command выполняет реальный `repository.Update(task)`, cleanup завершается и directories отсутствуют.
- **TF-AC-03:** Минимум два captured in-flight save failures из разных sealed snapshots и boundary-observable delete failure не скрываются; terminal delete failure содержит operation/path; combined failures дают один aggregate с каждой из трёх причин ровно один раз; existing inner `DisposableList.Dispose()` swallowing не расширяется этой spec и явно остаётся accepted limitation.
- **TF-AC-04:** `BaseModelTests` использует TUnit async disposal; Headless cleanup остаётся внутри dispatcher до session disposal; четыре global-state restoration path выполнены во вложенном `finally` даже при cleanup fault.
- **TF-AC-05:** Fixture без `Connect()`/repository очищается успешно, повторно идемпотентна и удаляет config/tasks/fixture paths.
- **TF-AC-06:** Все 147 старых `.CleanTasks()` call sites в 21 файле мигрированы; `rg -n "\.CleanTasks\(\)" src/Unlimotion.Test` не находит совпадений; два manual pre-dispose удалены.
- **TF-AC-07:** New lifecycle class содержит минимум три обязательных regression и проходит targeted run и 20 последовательных запусков с `--maximum-parallel-tests 1`.
- **TF-AC-08:** Full `Unlimotion.Test` выполняет минимум 603 tests, а `Unlimotion.UiTests.Headless` — минимум 31 test; оба serial suites PASS в существующем 30-minute CI budget, bounded local runs не timeout и logs не содержат fixture `FileNotFoundException`/`Unobserved task exception`.
- **TF-AC-09:** Diff ограничен этой spec, `src/Unlimotion.Test/**`, `src/Unlimotion.ViewModel/TaskItemViewModel.cs` и новым `src/Unlimotion.ViewModel/AssemblyInfo.cs`; workflow, UI behavior и unrelated emoji assertion не меняются.
- **TF-AC-10:** Fix PR green/ready/merged; PR #274 body обновлён точной root cause/validation/link на fix PR, branch обновлена от merge, required checks green, PR ready/merged до Stage 2 production EXEC.

### Tests to add/update
- New `MainWindowViewModelFixtureLifecycleTests`:
  - `CleanTasksAsync_ConcurrentCallersWaitForInFlightSaveBeforeDirectoryDeletion`;
  - `CleanTasksAsync_RepeatedCallAfterSaveAndDeleteFailuresReturnsSameAggregate`: два controlled saves на разных task VM сигнализируют `started` и остаются pending до seal, затем после общего `release` faulted двумя различимыми exceptions; injected test-only delete operation исчерпывает все три попытки; test проверяет ровно три причины без потерь/дублей, operation/path, один cleanup task и отсутствие повторного delete при повторном caller;
  - `CleanTasksAsync_UnconnectedFixtureDeletesOwnedPathsOnce`.
- Failure injection обязателен и остаётся внутри fixture/test assembly: cleanup operations/deletion delegate можно заменить в regression, normal constructor использует real filesystem.
- Каждая controlled replacement `ReactiveCommand` имеет test-owned `ThrownExceptions` subscription; subscription и command dispose только после bounded observation всех её invocations.
- Update all direct cleanup consumers and `BaseModelTests` lifecycle signatures.
- No new FlaUI/video test: no production UI behavior change.

### Characterization baseline
- `rg` inventory: 147 old calls / 21 files.
- PR #274 run attempt 1: 600 total, 599 pass, 1 unrelated UI failure, three fixture `FileNotFoundException` unobserved exceptions.
- PR #274 run attempt 2: no assertion result, 30m timeout after assembly start.
- В текущей сессии baseline Headless run прошёл 31 test; durable log не сохранён, поэтому `--minimum-expected-tests 31` заново подтверждает count в EXEC evidence.
- Unchanged workflow already serializes tests.

### Commands
Evidence directory, bounded process helper и restore:

```powershell
$evidenceDir = Join-Path (Get-Location) 'artifacts/test-fixture-lifecycle'
New-Item -ItemType Directory -Force $evidenceDir | Out-Null

function Stop-ProcessTree([int]$ProcessId) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId = $ProcessId" |
        ForEach-Object { Stop-ProcessTree -ProcessId $_.ProcessId }
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Invoke-BoundedDotnetTest {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [int] $TimeoutSeconds,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [switch] $ExpectFailure
    )

    $stdout = Join-Path $evidenceDir "$Name.stdout.log"
    $stderr = Join-Path $evidenceDir "$Name.stderr.log"
    $process = Start-Process dotnet `
        -ArgumentList $Arguments `
        -NoNewWindow `
        -PassThru `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-ProcessTree -ProcessId $process.Id
        throw "Timed out after $TimeoutSeconds seconds: $Name"
    }

    $process.WaitForExit()
    Get-Content $stdout
    if (Test-Path $stderr) { Get-Content $stderr }
    if ($ExpectFailure -and $process.ExitCode -eq 0) {
        throw "Expected failing test run succeeded unexpectedly: $Name"
    }
    if (-not $ExpectFailure -and $process.ExitCode -ne 0) {
        throw "dotnet test failed with exit code $($process.ExitCode): $Name"
    }
}

dotnet restore src/Unlimotion.Test/Unlimotion.Test.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet restore tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Build после добавления tests и минимального compile seam:

```powershell
dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj `
  -c Debug `
  --no-restore `
  -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Deterministic RED до implementation. Test использует assertion message `cleanup completed before controlled save release`; timeout, crash или другой failure не принимаются как RED:

```powershell
Invoke-BoundedDotnetTest `
  -Name 'targeted-red' `
  -TimeoutSeconds 180 `
  -ExpectFailure `
  -Arguments @(
      'test', 'src/Unlimotion.Test/Unlimotion.Test.csproj',
      '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false', '--',
      '--treenode-filter', '/*/*/MainWindowViewModelFixtureLifecycleTests/CleanTasksAsync_ConcurrentCallersWaitForInFlightSaveBeforeDirectoryDeletion',
      '--minimum-expected-tests', '1',
      '--maximum-parallel-tests', '1', '--output', 'Detailed')

$redLog = Join-Path $evidenceDir 'targeted-red.stdout.log'
if (-not (Select-String -LiteralPath $redLog -SimpleMatch 'cleanup completed before controlled save release' -Quiet)) {
    throw 'Targeted RED did not fail at the expected lifecycle assertion.'
}
```

Targeted lifecycle GREEN после implementation:

```powershell
Invoke-BoundedDotnetTest `
  -Name 'targeted-green' `
  -TimeoutSeconds 180 `
  -Arguments @(
      'test', 'src/Unlimotion.Test/Unlimotion.Test.csproj',
      '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false', '--',
      '--treenode-filter', '/*/*/MainWindowViewModelFixtureLifecycleTests/*',
      '--minimum-expected-tests', '3',
      '--maximum-parallel-tests', '1', '--output', 'Detailed')
```

Stress:

```powershell
1..20 | ForEach-Object {
    Invoke-BoundedDotnetTest `
      -Name ('stress-{0:D2}' -f $_) `
      -TimeoutSeconds 180 `
      -Arguments @(
          'test', 'src/Unlimotion.Test/Unlimotion.Test.csproj',
          '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false', '--',
          '--treenode-filter', '/*/*/MainWindowViewModelFixtureLifecycleTests/*',
          '--minimum-expected-tests', '3',
          '--maximum-parallel-tests', '1', '--output', 'Detailed')
}
```

Full workflow equivalent:

```powershell
Invoke-BoundedDotnetTest `
  -Name 'full-unlimotion' `
  -TimeoutSeconds 1200 `
  -Arguments @(
      'test', 'src/Unlimotion.Test/Unlimotion.Test.csproj',
      '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false', '--',
      '--minimum-expected-tests', '603',
      '--maximum-parallel-tests', '1', '--output', 'Detailed')

Invoke-BoundedDotnetTest `
  -Name 'full-headless' `
  -TimeoutSeconds 300 `
  -Arguments @(
      'test', 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj',
      '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false', '--',
      '--minimum-expected-tests', '31',
      '--maximum-parallel-tests', '1', '--output', 'Detailed')

$greenLogs = Get-ChildItem $evidenceDir -Filter '*.log' |
    Where-Object Name -Match '^(targeted-green|stress-\d+|full-(unlimotion|headless))\.(stdout|stderr)\.log$'
$greenLogText = ($greenLogs | ForEach-Object {
    Get-Content -LiteralPath $_.FullName -Raw
}) -join "`n"
$lifecycleMarkers = 'Unobserved task exception|FileNotFoundException[\s\S]{0,2000}MainWindowViewModelFixture_|MainWindowViewModelFixture_[\s\S]{0,2000}FileNotFoundException'
if ($greenLogText -match $lifecycleMarkers) {
    throw 'Lifecycle exception marker found in validation logs.'
}
```

Static scope gates:

```powershell
$oldCleanupCalls = @(rg -n "\.CleanTasks\(\)" src/Unlimotion.Test)
$oldCleanupExit = $LASTEXITCODE
if ($oldCleanupExit -eq 0) {
    $oldCleanupCalls
    throw 'Synchronous CleanTasks call sites remain.'
}
if ($oldCleanupExit -ne 1) {
    throw "rg inventory failed with exit code $oldCleanupExit"
}

$allowedPatterns = @(
    '^specs/2026-07-17-test-fixture-lifecycle\.md$',
    '^src/Unlimotion\.Test/',
    '^src/Unlimotion\.ViewModel/TaskItemViewModel\.cs$',
    '^src/Unlimotion\.ViewModel/AssemblyInfo\.cs$'
)

function Assert-Allowlisted([string[]] $Paths) {
    $unexpected = @($Paths | Where-Object {
        $path = $_
        -not ($allowedPatterns | Where-Object { $path -match $_ })
    })
    if ($unexpected.Count -gt 0) {
        $unexpected
        throw 'Diff contains paths outside the approved allowlist.'
    }
}

$status = @(git status --short)
if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
$status

$worktreePaths = @(git diff --name-only origin/main)
if ($LASTEXITCODE -ne 0) { throw 'git diff origin/main failed.' }
$untrackedPaths = @(git ls-files --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
Assert-Allowlisted -Paths @(($worktreePaths + $untrackedPaths) | Select-Object -Unique)

git diff --check
if ($LASTEXITCODE -ne 0) { throw 'Worktree diff check failed.' }

# После intentional staging implementation paths:
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
$stagedPaths = @(git diff --cached --name-only origin/main)
if ($LASTEXITCODE -ne 0) { throw 'Staged path inventory failed.' }
Assert-Allowlisted -Paths $stagedPaths

# После commit implementation:
$committedPaths = @(git diff --name-only origin/main...HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Committed path inventory failed.' }
Assert-Allowlisted -Paths $committedPaths
git diff --check origin/main...HEAD
if ($LASTEXITCODE -ne 0) { throw 'Committed diff check failed.' }
```

Expected `rg` result: exit code 1/no matches; exit 0 и infrastructure error fail closed. Allowlist проверяет tracked, untracked, staged и committed paths: this spec, `src/Unlimotion.Test/**`, `src/Unlimotion.ViewModel/TaskItemViewModel.cs`, `src/Unlimotion.ViewModel/AssemblyInfo.cs`.

Test loop stop rules:
- Targeted test timeout uses `WaitAsync` and fails; it does not cancel/release cleanup invisibly.
- One RED observation is enough before implementation; do not use probabilistic repetition as root-cause proof.
- Any post-fix lifecycle regression failure stops full-suite runs.
- Full suite timeout or lifecycle exception after GREEN target stops delivery and reopens diagnosis.
- Unrelated UI assertion gets isolated rerun/evidence and separate scope; required check still must become green before merge.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| TF-AC-01 | concurrent/success/failure idempotency tests | inspect shared-task implementation | targeted log | — |
| TF-AC-02 | controlled real-storage in-flight save + post-seal mutation | RED/GREEN comparison | `targeted-red/green` logs | — |
| TF-AC-03 | mandatory combined save/delete failure regression | inspect aggregate entries/path | targeted log + diff | Existing inner `DisposableList` swallowing is documented limitation |
| TF-AC-04 | affected base/Headless tests + nested-finally source assertions | inspect dispatcher/global-state ordering | full logs | — |
| TF-AC-05 | unconnected fixture regression | inspect null-repository branch | targeted log | — |
| TF-AC-06 | build + zero-match `rg` | compare expected 147/21 inventory | static-check output | — |
| TF-AC-07 | targeted + 20x stress with minimum count | bounded elapsed time | `stress-*.log` | — |
| TF-AC-08 | two bounded full serial runs with minimum counts | search logs for lifecycle markers | full logs + Actions URL | — |
| TF-AC-09 | pre/post-commit scope gates | allowlist review | Post-EXEC review | — |
| TF-AC-10 | GitHub required checks + PR #274 body/ancestry | inspect root-cause/link/check states | two PR URLs/check summaries | — |

## 12. Риски и edge cases
- Delayed producer может попытаться сохранить после cleanup start. Mitigation: same-lock atomic seal; post-seal mutation regression.
- 147 mechanical conversions могут оставить один unawaited finally. Mitigation: zero-match `rg`, compile, full suite.
- `async void` может появиться при неверной конверсии lambda/method. Mitigation: signatures review; разрешены только `async Task`/`ValueTask` lifecycle paths.
- Cleanup exception в прямом C# `finally` может заменить body exception. Mitigation: accepted limitation явно указан; cleanup errors сохраняются внутри своего aggregate, а global state восстанавливается nested `finally`. TUnit `IAsyncDisposable` path отдельно проверяется.
- Delete retry может удлинить teardown. Mitigation: bounded three attempts, async backoff, path-specific failure.
- Headless session может быть disposed раньше fixture. Mitigation: cleanup остаётся внутри dispatcher; explicit regression/review.
- Attempt-2 timeout может иметь другую причину. Mitigation: не заявлять доказанную идентичность; требовать deterministic regression и полный post-fix green suite.
- Unrelated emoji UI flake может снова сделать required check red. Mitigation: isolate and address only через отдельную approved package; не ослаблять assertion в этом diff.
- Production source switch имеет похожий потенциальный risk. Mitigation: отдельный follow-up spec/evidence, без scope expansion здесь.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Почему всё же меняется `TaskItemViewModel`? | Root проявился в fixture | Без same-lock producer seal queued 2-second repeater callback может появиться после snapshot; internal friend-only seam не меняет normal runtime | mitigated |
| Почему нельзя просто увеличить timeout? | Видимый симптом — 30m timeout | Attempt 1 уже показывает delete/read race; timeout не добавляет await и не устраняет unobserved exceptions | mitigated |
| Почему менять 147 мест? | Большой mechanical diff | Blocking adapter сохраняет неправильный lifecycle; compile + zero-match + full suite контролируют migration | mitigated |
| Как доказать, что это не вероятностная попытка? | Полный suite нестабилен | Controlled `started/release` interleaving задаёт детерминированный RED/GREEN | mitigated |
| Не скрываем ли отдельную UI-флейку? | Attempt 1 содержит UI failure | Она явно non-goal и остаётся required-check blocker, если повторится | mitigated |

### Rework Prevention Checklist
- User-visible output назван: green deterministic/full CI evidence и merge sequencing.
- Каждый observable scenario связан с AC/evidence.
- Все agent decisions зафиксированы; product/runtime choice не скрыт.
- Вероятные objections перечислены и закрыты.
- Tester/architect/delivery roles обязательны; UX marked not applicable с причиной.
- AC проверяют выполненный результат, а не только подготовительные шаги.
- EXEC содержит RED/GREEN, stress, full-suite, static scope и GitHub proof path.

## 13. План выполнения
1. Получить отдельную точную approval-фразу для этой child spec — выполнено 2026-07-18.
2. Freshness gate: fetch, проверить `origin/main` base/branch, clean tree, PR #274 state.
3. Добавить deterministic regression и минимальный compile seam; зафиксировать ожидаемый RED.
4. Реализовать same-lock atomic producer seal в `TaskItemViewModel`, friend-only internal seam, shared async cleanup owner и dispose/delete error contract.
5. Перевести `BaseModelTests` и все 147 call sites; удалить manual pre-dispose, сохранить unconnected-fixture path и вложенное восстановление четырёх global-state owners.
6. Запустить build, bounded targeted GREEN, 20x stress, minimum-count/static/log-marker gates.
7. Запустить full `Unlimotion.Test` и Headless serially; разобрать любой failure без scope masking.
8. Выполнить Post-EXEC review и independent re-review после fixes.
9. Commit/push; открыть draft PR с Summary/Changes/Validation/Risks-Rollback/Links; дождаться green checks, перевести ready и merge.
10. Обновить body PR #274 точной root cause, validation evidence и ссылкой на merged fix PR; обновить branch от merged `main`, проверить ancestry и rerun required checks; только green PR перевести ready и merge.
11. Вернуться на `fix/status-availability-contract`, rebase на новый `origin/main`, повторить Stage 2 baseline и начать уже утверждённый Stage 2 EXEC.

## 14. Открытые вопросы
Нет открытых design/product вопросов. Обязательная отдельная approval-фраза `Спеку подтверждаю` получена 2026-07-18.

Утверждённый дизайн уже включает ровно один узкий production-assembly seam: internal same-lock seal в `TaskItemViewModel` плюс friend declaration. Если deterministic RED или корректный fix потребуют любого более широкого production seam, работа останавливается для новой review/approval.

## 15. Соответствие профилю
- Профиль: testing-baseline + testing-dotnet + dotnet-desktop-client + CI delivery + review loops.
- Выполненные требования профиля:
  - TUnit filters используют `--treenode-filter`;
  - test-first deterministic regression;
  - local override учтён: Headless suite обязателен, хотя UI behavior не меняется;
  - UI video явно N/A с причиной;
  - exact commands, stop rules, evidence paths и existing CI topology зафиксированы;
  - production/test boundary и rollback заданы;
  - Post-SPEC/Post-EXEC review предусмотрены.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-17-test-fixture-lifecycle.md` | Этот contract и evidence | QUEST child-spec |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | Same-lock save-admission gate и cached internal sealed snapshot | Закрыть delayed-producer race без изменения normal runtime path |
| `src/Unlimotion.ViewModel/AssemblyInfo.cs` (new) | `InternalsVisibleTo("Unlimotion.Test")` | Ограничить lifecycle seam тестовой assembly |
| `src/Unlimotion.Test/MainWindowViewModelFixture.cs` | async shared cleanup, seal/drain, null-repository branch, injected delete boundary, surfaced failures, `IAsyncDisposable` | Root fix и детерминированные regressions |
| `src/Unlimotion.Test/MainWindowViewModelFixtureLifecycleTests.cs` (new) | Controlled RED/GREEN lifecycle tests | Deterministic proof |
| `src/Unlimotion.Test/MainWindowViewModelTests.cs` | `BaseModelTests : IAsyncDisposable`; awaited projection cleanup; remaining direct calls | TUnit lifecycle owner |
| `src/Unlimotion.Test/LocalizationDisplayDefinitionTests.cs` | awaited cleanup; remove manual pre-dispose; nested culture restore | Single owner и guaranteed global-state restore |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | awaited cleanup и nested application-font restore | Global-state restore even on cleanup fault |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | awaited cleanup и nested requested-theme restore | Global-state restore even on cleanup fault |
| 19 consumer files below, кроме `MainWindowViewModelTests.cs` и `LocalizationDisplayDefinitionTests.cs`; два из 19 раскрыты отдельными строками выше | mechanical awaited cleanup migration | Remove all synchronous callers |

Exact remaining consumer inventory:
- `BreadcrumbEmojiUiTests.cs`
- `MainControlAvailabilityUiTests.cs`
- `MainControlDateQuickSelectionUiTests.cs`
- `MainControlFilterToolbarResponsiveUiTests.cs`
- `MainControlNewTaskDeadlineUiTests.cs`
- `MainControlResetFiltersUiTests.cs`
- `MainControlRelationPickerUiTests.cs`
- `MainControlTaskCardLayoutUiTests.cs`
- `MainControlTabsOverflowUiTests.cs`
- `MainControlTaskStatusIconUiTests.cs`
- `MainControlTreeCommandsUiTests.cs`
- `MainScreenLoadingUiTests.cs`
- `PackageUpdateCompatibilityUiTests.cs`
- `MainControlWantedUiTests.cs`
- `RoadmapGraphUiTests.cs`
- `SettingsControlResponsiveUiTests.cs`
- `TaskImportanceUiTests.cs`
- `TaskListRepeaterMarkerUiTests.cs`
- `ToastNotificationUiTests.cs`

Explicitly unchanged:
- `.github/workflows/tests.yml`;
- все production files, кроме точных `src/Unlimotion.ViewModel/TaskItemViewModel.cs` и нового `src/Unlimotion.ViewModel/AssemblyInfo.cs`;
- README/specs other than this child spec during its delivery.

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Fixture cleanup | synchronous fire-and-forget boundary | awaitable single-owner lifecycle |
| Save admission | delayed callbacks могут начать save после snapshot | same-lock irreversible seal; post-seal callbacks are no-op |
| Pending saves | API exists but teardown ignores it | one cached sealed snapshot полностью awaited before delete |
| Repeated cleanup | bool early return, second caller cannot await first | same shared task/result/fault |
| Delete failure | swallowed after three sleeps | bounded async retry then explicit path error |
| TUnit teardown | `IDisposable` | `IAsyncDisposable` |
| Headless order | sync cleanup in finally | awaited cleanup in dispatcher before session dispose |
| CI response | retry/timeout ambiguity | deterministic regression + unchanged full workflow proof |
| Production behavior | autosave has no teardown admission state | normal autosave remains unchanged; only friend test fixture invokes internal seal |

## 18. Альтернативы и компромиссы
- Вариант: повысить workflow timeout.
  - Плюсы: минимальный diff.
  - Минусы: не устраняет delete/read race и unobserved exceptions.
  - Решение: отклонён.
- Вариант: ловить `FileNotFoundException` в `FileTaskStorage.GetAll()`.
  - Плюсы: скрывает конкретный stack.
  - Минусы: маскирует нарушение ownership, меняет production semantics, не гарантирует отсутствие hang.
  - Решение: отклонён.
- Вариант: оставить sync `CleanTasks()` и блокировать async через `.GetAwaiter().GetResult()`.
  - Плюсы: меньше caller edits.
  - Минусы: deadlock risk на dispatcher, не выражает TUnit lifecycle, сохраняет двусмысленное ownership.
  - Решение: отклонён.
- Вариант: изменить production disposal/source switching одновременно.
  - Плюсы: потенциально закрывает более широкий класс риска.
  - Минусы: нет доказанной production потери, другой контракт/риск/тесты, блокирует точечный CI fix.
  - Решение: отдельный MEDIUM follow-up.
- Выбранное решение: async fixture owner + минимальный internal same-lock producer barrier в `TaskItemViewModel` + deterministic interleaving test. Барьер закрывает очередь delayed callbacks, остаётся недоступным public/runtime callers и даёт проверяемый rollback.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Root problem, scope, evidence, goals/non-goals заданы |
| B. Качество дизайна | 6-10 | PASS | Async owner, same-lock cached seal, drain и error contract конкретны |
| C. Безопасность изменений | 11-13 | PASS | Узкий internal production seam, exact allowlist, rollback и stop rules явные |
| D. Проверяемость | 14-16 | PASS | Deterministic RED/GREEN, stress, full CI и static gates связаны с AC |
| E. Готовность к автономной реализации | 17-19 | PASS | Independent reviews закрыты; commands, sequencing и stop conditions однозначны; approval — отдельный governance gate |
| F. Соответствие профилю | 20 | PASS | TUnit/.NET/Headless/CI governance отражены |

Итог: `ГОТОВО`. Production EXEC остаётся запрещён только до отдельной точной approval-фразы.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | One-root lifecycle package; narrow internal seam и non-goals явные |
| 2. Понимание текущего состояния | 5 | Source, callsite и two-attempt CI evidence |
| 3. Конкретность целевого дизайна | 5 | Same-lock seal, outer drain, ordering/errors и consumer migration |
| 4. Безопасность (миграция, откат) | 5 | Atomic migration, exact production boundary, revert path и stop rules |
| 5. Тестируемость | 5 | Controlled interleaving, stress, full suites |
| 6. Готовность к автономной реализации | 5 | Independent fix/re-review PASS; exact commands/evidence/delivery handoff заданы |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению после обязательного approval gate.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | not applicable | Production workflow/data не меняются | PASS | Scope boundary review only |
| UX / designer | not applicable | User UI/layout/copy не меняются | PASS | Video/visual N/A зафиксировано |
| Tester / validation | applicable | Deterministic RED/GREEN и full evidence достаточны? | PASS | Final adversarial audit подтвердил non-masking RED, ReactiveCommand observer, mixed disposal, fail-closed scope и все 10 AC |
| Developer / architect | applicable | Ownership, async ordering и failure semantics coherent? | PASS | Same-lock cached seal и outer flattened drain прошли повторное independent review |
| Delivery / operations / security | applicable | CI/PR sequencing и scope safe? | PASS | Restore/timeouts/logs/allowlist и fix PR → PR #274 → Stage 2 прошли повторное review |

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: final spec, source/callsite inventory, delayed producer paths, TUnit package contract, PR #274 CI attempts, validation commands и delivery sequencing
- Decision: запросить отдельную user approval; EXEC до неё не начинать
- Review passes:
  - Scope/Evidence pass: self-review PASS
  - Contract pass: independent architect review PASS
  - Adversarial risk pass: same-lock producer barrier и multi-fault drain re-review PASS
  - Role-Based pass: tester/validation + architect + delivery PASS
  - Re-review after fixes / Fix and re-review: architect, delivery и final adversarial consistency verdicts PASS
  - Stop decision: approval получен; EXEC разрешён в exact allowlist, остальные stop rules остаются активны
- Evidence inspected: source lines, 147/21 inventory, delayed throttle producers, workflow, TUnit 1.44 package XML, local resolved SDK, GitHub run attempts/logs
- Depth checklist:
  - Scope drift / unrelated changes: spec-only
  - Acceptance criteria: 10 AC mapped
  - User-observable scenarios / Decision ledger / Expected objections: заполнены
  - Validation evidence: before evidence + exact after plan
  - Unsupported claims: attempt-2 causation explicitly limited
  - Regression / edge case: concurrent/repeated/faulted caller, two nested save faults, delete failure, unconnected fixture, dispatcher/global-state ordering
  - Comments/docs/changelog: PR handoff only; README/release notes N/A
  - Hidden contract change: только явно описанный internal `TaskItemViewModel` seal, доступный friend test assembly
  - Manual-review challenge: проверить lock linearization, flattened outer drain, failure aggregation и every async caller signature
- No-findings justification: финальные independent architect и delivery/validation re-reviews повторно проверили исправленные HIGH/MEDIUM findings; открытых findings не осталось.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | architecture | Initial snapshot/quiescence design не закрывал delayed producers | Добавить same-lock cached seal и narrow friend-only seam | fixed; re-review PASS |
| HIGH | delivery/validation | Initial scope/table/commands не покрывали production seam, unconnected fixture, global restores и false-green gates | Расширить exact contract, обязательные regressions и bounded evidence commands | fixed; re-review PASS |
| HIGH | deterministic RED | Early assertion мог быть замаскирован post-release storage fault; replacement command не наблюдал `ThrownExceptions` | Record-first RED, bounded non-throwing observation, exact final assertion и test observer | fixed; adversarial re-review PASS |
| MEDIUM | failure semantics | Nested `Task.WhenAll` мог потерять несколько save faults | Один outer drain + `Exception.Flatten()` + two-save/delete regression | fixed; re-review PASS |
| MEDIUM | traceability/gates | Scenario-to-AC links, mixed `DisposeAsync` coverage и static checks были неполными/fail-open | Исправить mapping, mixed caller regression и fail-closed path/rg gates | fixed; adversarial re-review PASS |
| LOW | audit metadata | Linter/rubric/journal описывали старый test-only design | Согласовать §19/журнал с final contract | fixed |

- Fixed before continuing: все initial independent findings исправлены; каждая substantive правка прошла повторное review
- Checks rerun: 22 обязательных H2 section, 14 fences, 10 AC, 7 PowerShell blocks без parse errors, zero trailing whitespace/tabs, stale-term scan и untracked-file whitespace check
- Needs human: только точная approval-фраза для child EXEC
- Residual risks / follow-ups: production source-switch drain; unrelated emoji flake

### Post-EXEC Review
- Статус: Не выполнен до EXEC
- Scope reviewed: Не применимо
- Decision: Не применимо до approval/EXEC
- Review passes: Не применимо
- Evidence inspected: Не применимо
- Depth checklist: Не применимо
- No-findings justification: Не применимо

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | phase | EXEC не начат | Выполнить полный Post-EXEC review после implementation/validation | follow-up |

- Fixed before final report: Не применимо
- Checks rerun: Не применимо
- Validation evidence: Не применимо
- Unrelated changes: Не применимо
- Needs human: approval child spec
- Residual risks / follow-ups: перечислены выше

## Approval
Получена отдельная точная фраза `Спеку подтверждаю` 2026-07-18. Статус: APPROVED FOR EXEC.

Approval master roadmap, Stage 1 или Stage 2 не распространяется автоматически на этот CI-lifecycle child EXEC.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Freshness/CI evidence gate | 1.00 | Нет для root package | Audit source, call sites и run attempts | Нет | Не применимо | PR #274 блокирует roadmap sequencing | CI logs, source, workflow |
| SPEC | Локализовать lifecycle owner | 0.99 | Attempt-2 не имеет stack | Ограничить fix lifecycle boundary и задать deterministic proof | Нет | Independent audit completed | Attempt 1 прямо показывает read-after-delete; initial load awaited | Source inventory |
| SPEC | Initial independent reviews | 1.00 | Нет | Исправить HIGH producer-barrier и delivery/validation gaps | Нет | Architect и delivery verdict `NEEDS-FIX` | Delayed 2-second producer требует same-lock seal; validation не должна давать false green | Эта spec, source, commands |
| SPEC | Перепроектировать child contract | 0.99 | Нет | Добавить narrow internal seal, outer flattened drain, unconnected/global-state и bounded evidence contracts | Нет | Findings приняты | Чисто test-only snapshot не может атомарно закрыть delayed callbacks | Эта spec |
| SPEC | Fix and re-review | 1.00 | Нет | Провести повторные architect и delivery/validation reviews | Нет | Оба independent verdict `PASS` | Same-lock/cached seal, multi-fault aggregation и delivery gates подтверждены | Эта spec |
| SPEC | Final adversarial consistency audit | 1.00 | Нет | Исправить RED masking, ReactiveCommand observer, AC mapping и fail-closed scope gates; повторить audit | Нет | Final verdict `PASS` | Исполняемый test/delivery contract проверен после последних правок | Эта spec, source, commands |
| SPEC | Approval gate | 1.00 | Нет | Перейти к EXEC в exact allowlist | Да | Пользователь: `Спеку подтверждаю` | Roadmap-required child approval получен 2026-07-18 | Эта spec |
