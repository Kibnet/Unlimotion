# Жизненный цикл storage в Headless AppAutomation-сессиях

## 0. Метаданные

- Тип (instruction stack): `delivery-task` (`model-behavior-baseline + quest-governance + collaboration-baseline + testing-baseline`) + profile `dotnet-desktop-client` + overlay `ui-automation-testing` + context `testing-dotnet`; SPEC governance — `quest-mode + spec-linter + spec-rubric + review-loops`.
- Владелец: Product Owner / активный пользователь.
- Масштаб: small, отдельный prerequisite-пакет для Stage 3 README reliability roadmap.
- Целевое семейство / behavior baseline: `GPT-5.6`; owner contract — `instructions/core/model-behavior-baseline.md`.
- Поверхность: `Work / Codex` (`Codex desktop`); product UI не меняется, меняется только lifecycle тестового AppAutomation host.
- Effective runtime: локальный Windows/.NET 10/TUnit/Avalonia.Headless; точный model/runtime не влияет на acceptance verdict.
- Eval baseline / evidence: две изолированные полные Headless-сессии на `origin/main@ec9b206db6930ef296313a14e2a440236807ba03`/Stage-3 HEAD завершили сами тесты (`12/12` и `4/4`), затем process crash с `TimeoutException` на `Tasks/.unlimotion.lock` и MTP exit `-532462766`.
- Целевой релиз / ветка: отдельная короткоживущая ветка `fix/headless-appautomation-storage-lifecycle` в отдельном clean worktree от актуального `origin/main`; merge является prerequisite для возобновления Stage 3. Dirty Stage-3 verifier/spec changes в prerequisite worktree не переносятся.
- Ограничения:
  - production storage, runtime UI, data format и product behavior не меняются;
  - не маскировать race через `catch TimeoutException`, увеличение timeout, sleep-only fix или ослабление Stage-3 gate;
  - не смешивать реализацию с distribution PR; сначала отдельный prerequisite PR и merge, затем rebase Stage 3;
  - EXEC разрешён после закрытия отдельного approval gate этой child spec; пользователь закрыл gate 2026-07-20 одним сообщением, явно назвав эту spec и Stage-3 amendment.
- Связанные ссылки:
  - master roadmap: `specs/2026-07-17-readme-reliability-roadmap.md`;
  - Stage-3 child spec: `specs/2026-07-18-distribution-support-contract.md`;
  - test host: `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAppLaunchHost.cs`;
  - full gate: `tests/Unlimotion.UiTests.Headless`.

## 1. Overview / Цель

Сделать владение конфигурацией и file-backed storage в каждой Headless AppAutomation-сессии явным: teardown сначала останавливает ViewModel producers, затем dispose-ит `UnifiedTaskStorage` (отключая watcher и отписывая callback), dispose-ит configuration и только после этого удаляет временный каталог.

Outcome contract:

- Success means:
  - каждая Headless launch-сессия владеет созданными `IConfigurationRoot` и `ITaskStorage` до конца своей жизни;
  - teardown идемпотентен и соблюдает порядок `ViewModel -> storage -> configuration -> temp data`;
  - несколько последовательных launch/dispose циклов переживают watcher throttle window без необработанного callback и без `.unlimotion.lock` timeout;
  - focused lifecycle regression и полный Headless suite проходят последовательно;
  - Stage 3 после merge/rebase снова может доказать `S3-AC-20` реальным полным PASS.
- Итоговый артефакт / output: отдельный merged test-infrastructure PR с lifecycle regression; никаких пользовательских UI/данных/release-изменений.
- Stop rules:
  - если fix требует production `UnifiedTaskStorage`, `FileTaskStorage`, `FileDbWatcher` или public API change, остановиться и обновить/повторно утвердить spec;
  - если после запрета новых deletion-generated callbacks воспроизводится callback, уже начавшийся до teardown, остановиться: quiescence production watcher требует новой spec/API и не входит в этот package;
  - если focused test не воспроизводит crash-path, не объявлять acceptance доказанной: усилить deterministic trigger внутри test-host scope либо запросить решение;
  - если после корректного ownership full Headless падает по иной причине, зафиксировать новый независимый blocker, не расширять scope молча.

## 2. Текущее состояние (AS-IS)

- `UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions` создаёт temp root и `MainWindowViewModel` для каждой сессии.
- `CreateHeadlessViewModel` создаёт `IConfigurationRoot` и вызывает `TaskStorageFactory.CreateFileStorage(...)`, но возвращает только ViewModel; configuration и созданный `UnifiedTaskStorage` не сохраняются как owned resources.
- `DisposeCallback` dispose-ит ViewModel и сразу вызывает `launchData.Dispose()`, который best-effort удаляет temp root.
- File watcher получает события удаления и через `MemoryCache`/`Task.Run` запускает отложенный callback примерно через секунду.
- Callback проходит через `UnifiedTaskStorage.TaskStorageOnUpdating(async void)` и пытается обновить отношения уже после удаления каталога. Открытие `.unlimotion.lock` повторяется до timeout; необработанное исключение завершает test host.
- Поведение воспроизводится изолированно после разного числа уже успешных тестов, поэтому последняя строка TUnit не определяет исходную сессию.
- Stage-3 diff не пересекается с crash-path. Ранее доставленный lifecycle fix `84777523` относится к `MainWindowViewModelFixture`, а не к AppAutomation test host.

## 3. Проблема

Headless AppAutomation host теряет ownership созданных configuration/storage и удаляет storage-каталог до `UnifiedTaskStorage.Dispose()`, который должен отключить watcher и отписать `Updating`; из-за этого удаление создаёт отложенный `async void` callback после завершения теста.

## 4. Цели дизайна

- Явное и единственное владение ViewModel, storage, configuration и temp data.
- Детерминированный обратный порядок teardown.
- Идемпотентность при normal dispose, partial launch failure и повторном callback.
- Focused regression, который проверяет именно delayed watcher boundary, а не случайный single-session green.
- Нулевое изменение production behavior и public API.

## 5. Non-Goals (чего НЕ делаем)

- Не меняем production storage/watchers/locking/retry policy.
- Не исправляем отдельно RavenDB eventual-consistency flake из полного Unit suite.
- Не меняем UI, status contract, persisted task schema или temp-path policy.
- Не ослабляем `S3-AC-20` и не принимаем случайный Headless green вместо lifecycle evidence.
- Не включаем `.gitattributes`/distribution identity fix: это отдельная поправка Stage 3.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `UnlimotionAppLaunchHost.cs` — session-owned holder для ViewModel, configuration, созданного storage и temp launch data; регистрация каждого ресурса сразу после создания; idempotent dispose в заданном порядке.
- `HeadlessSessionStorageLifecycleTests.cs` — три regression cases: induced delayed watcher race, launch failure после storage creation и повторный holder cleanup; класс сериализован существующим `DesktopUi` constraint.
- Эта spec — exact scope, evidence и delivery journal prerequisite-пакета.

### 6.2 Детальный дизайн

- Session lifetime holder передаётся в `CreateHeadlessViewModel`; configuration, storage и ViewModel регистрируются в нём непосредственно после каждого успешного создания. Возврат bundle только после полной initialization запрещён, потому что не закрыл бы exception между созданием ресурсов и return.
- При успешном teardown сначала dispose-ится ViewModel, чтобы остановить UI/reactive producers; затем фактический `IDisposable` storage, чтобы вызвать `UnifiedTaskStorage.Dispose()`, отключить watcher, отписать `Updating` и dispose-ить task objects; затем dispose-ится configuration и только после этого удаляется temp root. Сам `FileDbWatcher` не объявляется `IDisposable` и spec этого не обещает.
- Partial initialization использует тот же holder: уже созданные ресурсы освобождаются один раз в обратном порядке.
- Повторный dispose является no-op; static `TaskWrapperViewModel.DefaultIsExpanded` восстанавливается независимо от исключения teardown.
- Holder выполняет все cleanup steps даже при surfaced cleanup exception. При launch failure primary sentinel exception остаётся в exception chain; уже подавленные внутри `DisposableList`/`launchData.Dispose()` ошибки не выдаются за observable contract.
- Focused delayed-race case до dispose явно изменяет/удаляет seeded task file через captured `vm.taskRepository.TaskTreeManager.Storage` path; recursive temp deletion не является единственным вероятностным trigger.
- `[NotInParallel("DesktopUi")]` обязателен: serial CLI не заменяет защиту shared `HeadlessRuntime`/static state при обычном будущем запуске.
- Visual planning artifact: `Не применимо` — пользовательский layout/interaction не меняется.
- UI test video evidence: `Не применимо` — исправляется headless lifecycle; доказательство — автоматизированный focused test и полный suite.
- Производительность: дополнительная работа ограничена одним deterministic `Dispose` на уже созданный session storage; runtime приложения не затронут.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| RED/GREEN delayed race | Удалить/изменить watched task file и dispose-ить 8 последовательных сессий | До fix isolated run падает non-zero на `.unlimotion.lock`; после fix process жив после 1.5 s и control session | Separate RED/GREEN logs | HSL-AC-02 |
| Partial/idempotent cleanup | Бросить sentinel после storage creation; вызвать сохранённый holder callback дважды | Primary sentinel сохранён, static state восстановлен, повторный cleanup no-op, control session работает | Два focused TUnit cases | HSL-AC-03 |
| Full Headless | Запустить весь Headless project serially | Suite и test host завершаются exit 0, без post-test crash | Два последовательных full PASS в разных result dirs | HSL-AC-04 |
| Stage-3 resume handoff | Rebase distribution branch после prerequisite merge | `S3-AC-20` снова проверяется без baseline teardown blocker | Stage-3 final-head full gate; downstream, не exit gate prerequisite | S3-AC-20 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Launch data создана, storage ещё нет | Initialization fails | Temp data удаляется один раз | No storage/VM -> safe no-op | Partial init |
| Configuration/storage созданы, VM preparation fails | Failure | Storage -> configuration -> temp cleanup | No VM -> remaining resources still owned; primary sentinel preserved | Partial path |
| Полная сессия | Normal dispose | VM -> storage -> configuration -> temp root | Repeated holder callback -> no-op | Primary path |
| Watcher event ещё не запущен | Session dispose | Storage disable/unsubscribe precedes directory deletion | Temp deletion не создаёт новый callback | Guaranteed boundary |
| Callback уже начал выполняться | Concurrent teardown | Focused/full stress должен остаться green | Если race остаётся — stop/new production-quiescence spec | Residual boundary |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Delivery isolation | agent | Отдельный prerequisite PR до Stage 3 | 1.00 | Смешанный PR ухудшит scope и rollback | Нет |
| Fix boundary | agent | Test-host ownership; production storage unchanged | 0.99 | Production change расширит риск | Нет |
| Teardown order | agent | ViewModel, storage, configuration, temp data | 0.99 | Иной порядок оставляет producer/watcher/config lifetime race | Нет |
| Acceptance strength | agent | RED/GREEN delayed case + launch-failure + double-callback cases + два full Headless PASS | 0.99 | Single green не доказывает flake fix | Нет |
| Child EXEC approval gate | user | Получено 2026-07-20: точная фраза `Спеку подтверждаю` с явным названием обеих specs | 1.00 | Без approval нарушился бы QUEST gate | Закрыт |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Headless session lifetime | `UnlimotionAppLaunchHost` | Явное ownership storage | Test-only; no migration | Focused lifecycle test |
| Test configuration | `WritableJsonConfigurationFabric` | Явное ownership/dispose после storage | Test-only; config schema unchanged | Partial/normal lifecycle cases |
| Production storage | `UnifiedTaskStorage`/`FileTaskStorage` | Без изменений | Полная совместимость | Exact diff allowlist |
| Temp test data | `UnlimotionAutomationLaunchData` | Удаление после storage dispose | Path/layout unchanged | Test completes after throttle |
| UI behavior | Existing Headless scenarios | Без изменений | Full regression | Full Headless suite |

## 7. Бизнес-правила / Алгоритмы

1. Ресурс регистрируется в session lifetime немедленно после успешного создания.
2. Teardown выполняется ровно один раз и в порядке `VM producers -> UnifiedTaskStorage disable/unsubscribe/dispose -> configuration -> temp files`.
3. Удаление temp root до storage dispose запрещено.
4. Искусственный timeout/retry/sleep в production storage не является допустимым исправлением.
5. Stage 3 остаётся paused до merge prerequisite и повторного полного green gate после rebase.

## 8. Точки интеграции и триггеры

- `CreateHeadlessLaunchOptions.BeforeLaunchAsync` создаёт/регистрирует configuration, storage и ViewModel как session resources.
- `HeadlessAppLaunchOptions.DisposeCallback` вызывает один session teardown.
- Ошибка initialization вызывает тот же idempotent teardown.
- Новый test запускает и dispose-ит несколько `DesktopAppSession` через существующий Headless runtime.

## 9. Изменения модели данных / состояния

- Persisted/product data: без изменений.
- Test-only calculated state: ссылки на owned ViewModel/storage/configuration/launch data и disposed flag внутри session lifetime.
- Public API: без изменений; новый holder остаётся private/internal test-host implementation detail.

## 10. Миграция / Rollout / Rollback

- Миграция не требуется.
- Rollout: отдельный clean worktree/branch, RED/GREEN focused validation, два full Headless PASS, independent review и merge. Stage-3 rebase — downstream handoff после завершения этого package, а не его exit criterion.
- Rollback: revert prerequisite PR; никаких persisted/customer data последствий.
- Если fix не проходит acceptance, revert test-host changes и оставить Stage 3 paused с исходным evidence.

## 11. Тестирование и критерии приёмки

- **HSL-AC-01 — ownership/order:** configuration и storage, созданные Headless session, сохраняются до teardown; порядок = VM -> storage disable/unsubscribe/dispose -> configuration -> temp deletion; surfaced cleanup steps выполняются полностью; repeated/partial cleanup безопасен.
- **HSL-AC-02 — TDD delayed-callback RED/GREEN:** до production-test-host fix новый isolated case явно создаёт watched task-file event, выполняет не менее 8 launch/dispose циклов и воспроизводит non-zero `.unlimotion.lock` crash. После fix тот же case ждёт не менее 1.5 секунды, process остаётся жив и финальная control session запускается/закрывается. Если RED не воспроизводится, trigger усиливается до implementation.
- **HSL-AC-03 — partial/idempotent focused cases:** отдельные tests (a) бросают sentinel через `afterViewModelPrepared` после storage creation и подтверждают primary exception, cleanup/static-state restoration/control launch; (b) вызывают сохранённый `HeadlessAppLaunchOptions.DisposeCallback` дважды и подтверждают no-op второго вызова/control launch. Класс имеет `[NotInParallel("DesktopUi")]`; focused discovery = минимум 3 tests.
- **HSL-AC-04 — full regression:** solution build PASS; три focused tests PASS; полный `Unlimotion.UiTests.Headless` проходит два раза подряд serially с exit 0 и без post-test host crash; каждый run хранит отдельный result directory/report.
- **HSL-AC-05 — scope/delivery:** diff содержит только exact 3-file allowlist, independent review и required checks PASS, prerequisite PR merged. После merge child package завершён; Stage-3 rebase/full gate является downstream `S3-AC-20`, не условием HSL completion.

Планируемые команды после approval:

```powershell
$evidenceRoot = Join-Path (Get-Location) 'artifacts/test-results/headless-storage-lifecycle'

# Clean-worktree bootstrap и RED на исходном test host.
dotnet restore src/Unlimotion.sln
dotnet build src/Unlimotion.sln --no-restore -m:1 /nr:false /p:UseSharedCompilation=false
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/HeadlessSessionStorageLifecycleTests/DelayedWatcherEvent_AfterDispose_DoesNotCrashHost" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed --no-progress --report-trx --report-html --results-directory "$evidenceRoot/focused-red"

# После implementation fix обязательно пересобрать изменённый host перед GREEN.
dotnet build src/Unlimotion.sln --no-restore -m:1 /nr:false /p:UseSharedCompilation=false
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/HeadlessSessionStorageLifecycleTests/*" --minimum-expected-tests 3 --maximum-parallel-tests 1 --output Detailed --no-progress --report-trx --report-html --results-directory "$evidenceRoot/focused-green"
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build --no-restore -- --maximum-parallel-tests 1 --output Detailed --no-progress --report-trx --report-html --results-directory "$evidenceRoot/full-1"
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build --no-restore -- --maximum-parallel-tests 1 --output Detailed --no-progress --report-trx --report-html --results-directory "$evidenceRoot/full-2"
git diff --check
git diff --name-only origin/main...HEAD
```

Stop rules: targeted loop ограничен 8 сессиями и 1.5-second boundary; full suite выполняется максимум два обязательных consecutive runs после успешного focused test. Любой повторный crash останавливает delivery и требует новой диагностики, а не бесконечного rerun.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| HSL-AC-01 | Failure + double-callback focused cases | Diff review exact order/config ownership | Focused TUnit reports + reviewed diff | — |
| HSL-AC-02 | Exact delayed method RED, затем тот же GREEN | Non-zero/`.unlimotion.lock` before; exit 0 after | `focused-red-confirmed/console.log`; `focused-green-retained` console/exit/TRX/HTML | — |
| HSL-AC-03 | Минимум 3 discovered focused tests + `DesktopUi` constraint | Sentinel/static/control-session evidence | `focused-green-retained` console/exit/TRX/HTML | — |
| HSL-AC-04 | Build + два serial full runs + full Unit | Exit codes и totals | `build-final-retained`, `full-1-retained`, `full-2-retained`, `unit-full-retained` | — |
| HSL-AC-05 | Scope/git/PR checks | Independent review и merge | PR/merge evidence | — |

Actual evidence хранится в `artifacts/test-results/headless-storage-lifecycle/`, игнорируется Git и доступно только локально. `focused-red` отклонён как test-only Avalonia cross-thread failure; `focused-red-lifecycle` отклонён как ложный green со слабым trigger; подтверждённый lifecycle RED сохранён в `focused-red-confirmed/console.log`.

## 12. Риски и edge cases

- Callback уже мог начаться до dispose: test-host fix гарантирует, что temp deletion не создаёт новые callbacks, но не обещает quiescence уже выполняющегося callback; focused/full stress покрывает residual, повторное воспроизведение требует stop/new spec.
- Partial launch может оставить storage без VM: ownership регистрируется сразу после creation.
- Double dispose может вызвать вторичное cleanup: idempotent flag обязателен.
- Static expansion flag может остаться изменённым при exception: восстановление выполняется в teardown независимо.
- Focused test может быть ложно зелёным, если не пересекает throttle: минимум 8 cycles + 1.5-second wait + control launch.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Почему не просто повторить full suite? | Crash вариативен и тесты уже PASS до падения host | Focused delayed-callback regression + два full runs | mitigated |
| Почему это отдельный PR? | Stage 3 уже большой и имеет exact distribution allowlist | Isolated prerequisite branch/rollback/review | mitigated |
| Не затронет ли fix production storage? | Stack trace проходит production types | Exact allowlist запрещает их изменение | mitigated |
| Зачем ждать 1.5 секунды? | Watcher callback throttled примерно на 1 секунду | Wait — assertion boundary после реального teardown, не fix mechanism | mitigated |
| Почему не гарантируется остановка любого callback? | `FileDbWatcher` запускает handler через `Task.Run` без join API | Гарантия сужена до disable/unsubscribe before deletion; in-flight callback — explicit stop/new-spec boundary | mitigated |

### Rework Prevention Checklist

- [x] Видимый outcome — стабильный exit 0 Headless host — назван.
- [x] Каждый сценарий имеет автоматизированное evidence.
- [x] Agent/user decisions зафиксированы.
- [x] Single-green и production catch/timeout alternatives запрещены.
- [x] Role-based independent Post-SPEC review прошёл; architecture, QA и governance verdicts = PASS.
- [x] Acceptance criteria описывают проверяемый результат.
- [x] EXEC имеет focused/full/delivery proof path.

## 13. План выполнения

1. Завершить independent Post-SPEC review и исправить deterministic findings.
2. Получить отдельный approval gate точной фразой `Спеку подтверждаю`; одно сообщение допустимо, если явно подтверждает эту spec и Stage-3 amendment.
3. Создать отдельный clean worktree и ветку `fix/headless-appautomation-storage-lifecycle` от свежего `origin/main`; перенести только approved child spec, не копируя dirty Stage-3 scripts/specs.
4. Зафиксировать before evidence и exact scope.
5. Сначала добавить три focused lifecycle tests; exact delayed method на старом host обязан дать RED/non-zero `.unlimotion.lock`, иначе усилить watched-file trigger и не начинать fix.
6. Реализовать session ownership и idempotent ordered teardown в test host.
7. Повторно build-ить solution/test host после fix, получить GREEN трёх focused tests и затем выполнить два consecutive full Headless runs с раздельными reports.
8. Провести independent code/test/scope review; исправления требуют повторного полного gate.
9. Commit/push/draft PR, дождаться required checks, merge.
10. После merge передать downstream handoff Stage 3: rebase и повторный `S3-AC-20`. Этот шаг не блокирует завершение prerequisite Post-EXEC.

## 14. Открытые вопросы

- Блокирующих design-вопросов нет.
- До EXEC требуется только явное закрытие approval gate этой child spec.

## 15. Соответствие профилю

- Профиль: `dotnet-desktop-client`, overlay `ui-automation-testing`, context `testing-dotnet`.
- Выполненные требования: минимальный test-host scope, TUnit `--treenode-filter`, serial Headless validation, lifecycle regression, full suite, отдельный PR/rollback.
- UI automation overlay: релевантен как test infrastructure; focused Headless coverage обязательна, visual/video evidence не применимо из-за отсутствия пользовательского визуального изменения.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-19-headless-appautomation-storage-lifecycle.md` | Approval, evidence и delivery journal | Auditable prerequisite |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAppLaunchHost.cs` | Явное ownership configuration/storage и ordered idempotent teardown | Disable/unsubscribe storage callback до temp deletion |
| `tests/Unlimotion.UiTests.Headless/Tests/HeadlessSessionStorageLifecycleTests.cs` | Delayed watcher RED/GREEN, partial sentinel и double-callback regressions | Не допустить post-test host crash и lifecycle leaks |

Таблица является exact allowlist. Любой production/runtime/docs/workflow path требует остановки, обновления spec и повторного approval.

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Resource ownership | Configuration и результат `CreateFileStorage` теряются | Configuration/storage принадлежат session lifetime |
| Teardown | VM -> temp delete | VM -> storage disable/unsubscribe/dispose -> configuration -> temp delete |
| Delayed watcher | Temp deletion создаёт callback после teardown | Storage отключён до удаления; новые deletion callbacks не создаются |
| Evidence | Full suite падает после успешных тестов | Focused lifecycle + два full exit 0 |
| Stage 3 | Заблокирован `S3-AC-20` | Возобновляется после merge/rebase |

## 18. Альтернативы и компромиссы

- Повторять suite до green: отвергнуто, потому что не доказывает устранение race.
- Catch `TimeoutException`/увеличить timeout: отвергнуто, потому что маскирует потерянное ownership.
- Менять production watcher/storage: отвергнуто без evidence необходимости; риск и scope выше.
- Включить fix в Stage-3 distribution PR: отвергнуто ради точного scope, review и rollback.
- Выбрано: test-host-owned lifecycle + focused deterministic regression в отдельном prerequisite PR.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, root cause и non-goals заданы |
| B. Качество дизайна | 6-10 | PASS | Ownership/order/partial/idempotent paths определены |
| C. Безопасность изменений | 11-13 | PASS | Production paths запрещены, rollback trivial |
| D. Проверяемость | 14-16 | PASS | Focused + repeated full + scope/delivery evidence |
| E. Готовность к автономной реализации | 17-19 | PASS | Exact scope/order/RED-GREEN/evidence/stop rules заданы; approval gate закрыт 2026-07-20 |
| F. Соответствие профилю | 20 | PASS | TUnit/Headless/delivery требования отражены |

Итог: `ГОТОВО` по static linter; independent re-review завершён с `PASS`, approval gate закрыт 2026-07-20.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один test-host lifecycle defect |
| 2. Понимание текущего состояния | 5 | Stack, ownership gap и timing подтверждены |
| 3. Конкретность целевого дизайна | 5 | Порядок teardown и failure paths заданы |
| 4. Безопасность | 5 | Production unchanged, separate rollback |
| 5. Тестируемость | 5 | Focused delayed boundary и два full runs |
| 6. Готовность к автономной реализации | 5 | Exact files, RED/GREEN, full evidence, rollback и stop rules определены |

Итоговый балл: `30 / 30`. Зона: `готово к автономному выполнению`; review и approval gates закрыты.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | not applicable | Product workflow меняется? | Не применимо | Product behavior отсутствует |
| UX / designer | not applicable | UI/layout меняется? | Не применимо | Только test lifecycle |
| Tester / validation | applicable | Доказан delayed callback и full stability? | PASS | RED/GREEN, 3 cases, discovery count, serialization, post-fix build и separate reports подтверждены re-review |
| Developer / architect | applicable | Ownership/order/partial cleanup корректны? | PASS | Immediate holder registration, configuration ownership, exact watcher semantics и in-flight boundary подтверждены |
| Delivery / operations / security | applicable | Scope/branch/rollback изолированы? | PASS | Clean worktree, exact 3-file scope, non-cyclic completion и approval-gate wording подтверждены |

### Post-SPEC Review

- Статус: `PASS`; architecture, QA и governance final re-reviews не нашли remaining actionable findings.
- Scope reviewed: эта spec, `UnlimotionAppLaunchHost`, `IConfigurationRoot`, `UnifiedTaskStorage.Dispose`, `FileDbWatcher` throttle/callback semantics, existing AppAutomation 1.5.7 session/options contract, TUnit filters/constraints и Stage-3 dependency sequence.
- Decision: spec готова к approval gate; EXEC остаётся запрещён до явного подтверждения пользователя.
- Review passes:
  - Scope/Evidence pass: exact 3-file allowlist sufficient; no `.csproj`/production change needed.
  - Contract pass: test host owns VM/storage/configuration/temp data; guarantees narrowed to disable/unsubscribe before deletion.
  - Adversarial risk pass: partial init, double callback, static state, in-flight callback, dirty-worktree carryover and report overwrite addressed.
  - Role-Based pass: architecture, QA и governance = PASS после correction cycle.
  - Re-review after fixes / Fix and re-review: PASS; immediate registration, watcher boundary, RED/GREEN build order, 3-case evidence, serialization и clean-worktree sequence подтверждены.
  - Stop decision: запросить approval; EXEC запрещён до его получения.
- Evidence inspected: two isolated host crashes, exact stack/source, `UnifiedTaskStorage.Dispose`, `FileDbWatcher.GetCachePolicy`, `DisposableList`, AppAutomation 1.5.7 lifecycle behavior and existing `DesktopUiConstraint` usage.
- Depth checklist: exact scope, ownership, partial/repeated paths, deterministic RED/GREEN, full-run evidence, downstream handoff, rollback and unsupported quiescence claim checked.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | ownership | Configuration была потеряна вместе со storage | Добавить configuration в holder и teardown order | fixed; PASS |
| HIGH | watcher contract | Spec обещала dispose watcher и остановку любого callback | Зафиксировать actual disable/unsubscribe и explicit in-flight stop boundary | fixed; PASS |
| HIGH | TDD evidence | Fix планировался раньше regression, а delayed trigger был вероятностным | Сначала induced watched-file RED, затем fix/GREEN | fixed; PASS |
| HIGH | focused coverage | Partial и repeated paths не имели отдельных tests/discovery count | Задать 3 exact cases, sentinel/double-callback/control session, minimum 3 | fixed; PASS |
| HIGH | sequencing | HSL completion зависела от downstream Stage-3 gate | Завершать package на собственном merge; Stage-3 оставить handoff | fixed; PASS |
| MEDIUM | test isolation | Serial CLI не защищал shared runtime/static state | Добавить `NotInParallel("DesktopUi")` | fixed; PASS |
| MEDIUM | evidence retention | Два full runs перезаписывали default report | Разделить focused/full result directories | fixed; PASS |
| MEDIUM | branch isolation | Dirty Stage-3 changes могли попасть в prerequisite branch | Создавать отдельный clean worktree от fresh main | fixed; PASS |
| LOW | profile | `ui-automation-testing` был применим, но отсутствовал в metadata stack | Добавить overlay | fixed; PASS |
| HIGH | partial ownership | Bundle-return alternative могла потерять ресурсы при exception до return | Передавать holder в factory и регистрировать каждый resource немедленно | fixed; PASS |
| MEDIUM | build evidence | Clean worktree не имел restore, а GREEN с `--no-build` использовал бы pre-fix binary | Добавить restore до RED и обязательный rebuild после fix | fixed; PASS |
| LOW | scope description | Test-file allowlist row упоминал только delayed case | Перечислить все три focused regressions | fixed; PASS |

- Fixed before continuing: все deterministic findings обоих review cycles внесены; код не изменялся.
- Checks rerun: canonical 22 H2 sections, balanced fences, 5/5 AC-to-test mappings, exact 3-file allowlist, command/filter/build-order review, `git diff --check`.
- No-findings justification: final architecture review подтвердил immediate resource registration/exact watcher semantics/3-file sufficiency; QA подтвердил TUnit filters, RED/GREEN build sequence, 3-case discovery, serialization и retained evidence; governance подтвердил non-cyclic ownership/clean delivery/approval semantics.
- Needs human: approval gate закрыт 2026-07-20 одним явным сообщением для обеих specs.
- Residual risks / follow-ups: известный RavenDB flake в final-head full Unit не воспроизвёлся (`830/830`); residual для этого package отсутствует.

### Post-EXEC Review

- Статус: `PASS` для локальной реализации и validation; HSL-AC-05 остаётся delivery gate до required checks и merge.
- Scope reviewed: approved spec, `git status --short`, exact host/test/spec diff, retained RED/GREEN/full/Unit/build evidence, docs/changelog impact и branch/base state.
- Decision: можно commit/push и открыть draft PR; package завершится только после checks и merge.
- Review passes:
  - Scope/Evidence pass: ровно три allowlisted файла; artifacts ignored; Stage-3 и production files отсутствуют.
  - Contract pass: immediate ownership configuration/storage/VM; teardown `VM -> storage -> configuration -> static restore -> temp`; public API/product UI/data unchanged.
  - Adversarial risk pass: partial init, same-reference primary exception, double callback, post-dispose forced delivery, real watcher throttle, in-flight boundary и post-report process crash проверены.
  - Role-Based pass: tester, developer/architect и delivery/operations reviews применимы и завершены; UX не применим, потому что visual state/flow не меняется.
  - Re-review after fixes / Fix and re-review: evidence-retention finding исправлен повторными focused/full runs с console/exit receipts; governance findings закрыты final build receipt, full Unit и точным evidence mapping.
  - Stop decision: локальный `PASS`; продолжить delivery, не считать package завершённым до merge.
- Role-Based Review Result:
  - Business analyst / domain workflow: не применимо, product workflow отсутствует.
  - UX / designer: не применимо, layout/visual state/interaction не меняются.
  - Tester / validation: `PASS`; честный RED до host fix, тот же GREEN, три cases и retained exit receipts проверены.
  - Developer / architect: `PASS`; immediate ownership, order, idempotence, exception semantics и production boundary проверены.
  - Delivery / operations / security: `PASS` для local gate; exact scope/rollback/evidence готовы, PR checks/merge ещё впереди.
- Evidence inspected:
  - `focused-red-confirmed/console.log`: exit `-532462766`, `.unlimotion.lock`, `TaskStorageOnUpdating`;
  - `focused-green-retained`: `3/3`, exit `0`, console/TRX/HTML;
  - `full-1-retained` и `full-2-retained`: `36/36` + `36/36`, оба exit `0`, console/TRX/HTML;
  - `unit-full-retained`: `830/830`, exit `0`, console/TRX/HTML;
  - `build-final-retained`: solution build exit `0`, 0 errors; `dotnet format --verify-no-changes` exit `0`; `git diff --check` PASS.
- Depth checklist:
  - Scope drift / unrelated changes: отсутствуют, exact 3-file allowlist.
  - Acceptance criteria: HSL-AC-01..04 локально PASS; HSL-AC-05 pending delivery.
  - User-observable scenarios / Acceptance-to-test matrix / Expected objections: стабильный process exit, partial/repeated cleanup и control launch закрыты; evidence paths синхронизированы.
  - Validation evidence: raw console/exit + TRX/HTML retained local-only; build/Unit также retained.
  - Unsupported claims: guarantee ограничена disable/unsubscribe before temp deletion; уже начавшийся callback не объявлен quiesced.
  - Regression / edge case: 8 cycles, 1.5-second boundary, sentinel, double callback, two full runs.
  - Comments/docs/changelog: новый комментарий только о test cleanup invariant; changelog не нужен для test-infrastructure fix.
  - Hidden contract change: public API, product behavior, persistence, config и production watcher не менялись.
  - Manual-review challenge: отсутствие process receipts было бы главным скрытым дефектом; исправлено повторными retained runs.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | evidence | TRX/HTML не доказывали отсутствие post-report process crash | Повторить focused/full с raw console и exit code | fixed; re-reviewed PASS |
| HIGH | build evidence | Final-tree solution build не был отражён auditable receipt | Сохранить final build console/exit и 0 errors | fixed; PASS |
| MEDIUM | evidence mapping | Exploratory cross-thread RED мог быть принят за lifecycle RED | Указать rejected runs и exact confirmed RED path | fixed; PASS |
| MEDIUM | full validation | Full Unit не был выполнен в prerequisite package | Выполнить serial full Unit и классифицировать flake | fixed; `830/830`, exit 0 |
| MEDIUM | UI evidence | Video fallback был только планом | Записать objective reason, commands, next-best evidence и local-only limitation | fixed; PASS |
| INFO | architecture/tests/scope | Оставшихся actionable findings нет | Нет | PASS |

- Fixed before final report: все deterministic post-EXEC findings исправлены; runtime code после GREEN не менялся.
- Checks rerun: retained focused `3/3`; retained full Headless `36/36` дважды; full Unit `830/830`; final solution build; format verify; diff/scope checks.
- Validation evidence: `artifacts/test-results/headless-storage-lifecycle/*-retained` и `focused-red-confirmed/console.log` (`local-only`, Git ignored).
- Video fallback: `Не применимо` — меняется только lifecycle test host, без визуально наблюдаемого UI state/flow; post-test process crash невозможно содержательно показать видео. Next-best evidence — exact команды из HSL-AC-04, confirmed RED console, focused GREEN, два full Headless console/TRX/HTML и exit-code receipts.
- Unrelated changes: отсутствуют.
- Needs human: нет до обычного PR review; новый product/API/UX choice не требуется.
- Residual risks / follow-ups: required GitHub checks и merge; in-flight callback до disable остаётся утверждённой stop/new-spec boundary.

## Approval

Получено 2026-07-20: `Спеку подтверждаю. Подтверждение относится к specs/2026-07-18-distribution-support-contract.md (LF amendment) и specs/2026-07-19-headless-appautomation-storage-lifecycle.md.`

Approval gate закрыт. EXEC выполняется последовательно: prerequisite merge перед возобновлением Stage 3.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность | Каких данных не хватает | Следующее действие | Нужен человек | Фактическое решение человека | Короткое объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Классифицировать full Headless crash | 1.00 | Нет | Зафиксировать отдельный prerequisite | Нет | Не применимо | Два isolated runs завершили тесты, затем упали на delayed `.unlimotion.lock` callback | Test output, source triage |
| SPEC | Локализовать ownership gap | 0.99 | Independent review | Проверить design/acceptance/scope | Нет | Не применимо | AppAutomation host теряет configuration/storage и удаляет temp root раньше storage disable/unsubscribe | Эта spec, source paths |
| SPEC | Изолировать delivery от Stage 3 | 1.00 | User approval | Провести review и запросить совместное approval документов | Да после review | Ещё не обращались | Отдельный PR сохраняет distribution allowlist и rollback | Эта spec, Stage-3 spec/master roadmap |
| SPEC | Исправить first-cycle Post-SPEC findings | 0.99 | Final re-review | Повторить architecture/QA/governance review | Нет | Три reviewers вернули NEEDS-FIX | Добавлены configuration ownership, exact watcher boundary, RED/GREEN three-case evidence, serialization, clean worktree и non-cyclic handoff | Эта spec, review verdicts |
| SPEC | Исправить re-review findings | 1.00 | Final short re-review | Проверить отсутствие remaining findings | Нет | Architecture/QA нашли immediate-registration и build-order gaps | Holder теперь регистрирует каждый ресурс сразу; clean worktree restore и post-fix rebuild обязательны; allowlist row отражает три cases | Эта spec, review verdicts |
| SPEC | Завершить final independent re-review | 1.00 | Только user approval | Запросить одно явное подтверждение обоих документов | Да | Architecture, QA и governance вернули PASS | 22 H2, 5/5 AC, exact scope, TDD/build/evidence/delivery checks PASS; код не изменён | Эта spec, Stage-3 amendment, reviewer verdicts |
| EXEC | Зафиксировать approval и начать prerequisite | 1.00 | Нет | Создать tests-first RED в clean worktree | Нет | Пользователь явно подтвердил обе названные specs 2026-07-20 | QUEST gate закрыт; ветка `fix/headless-appautomation-storage-lifecycle` создана от `origin/main@ec9b206d` | Эта spec, clean worktree, user approval |
| EXEC | Подтвердить clean baseline | 1.00 | Нет | Добавить regression tests на неизменённом host | Нет | Не применимо | `dotnet restore` PASS; полный solution build PASS за 6:01, 0 ошибок; существующие warnings не относятся к scope | Restore/build output |
| EXEC | Получить валидный tests-first RED | 1.00 | Нет | Реализовать session lifetime holder | Нет | Не применимо | Cross-thread run отклонён; слабый trigger дал ложный green и был усилен. Два observed process runs завершились `-532462766`; один retained run содержит два `.unlimotion.lock` exceptions из `TaskStorageOnUpdating` до host fix | `artifacts/test-results/headless-storage-lifecycle/focused-red-confirmed/console.log` |
| EXEC | Реализовать ownership и получить focused GREEN | 1.00 | Нет | Выполнить два full Headless run | Нет | Не применимо | Private holder немедленно регистрирует configuration/storage/VM; teardown idempotent и выполняет VM -> storage -> configuration -> static restore -> temp. Focused class PASS `3/3`, exit 0 | Host/test diff, `artifacts/test-results/headless-storage-lifecycle/focused-green-retained` |
| EXEC | Подтвердить повторную full stability | 1.00 | Full validation/review | Запустить full Unit/final build и Post-EXEC review | Нет | Не применимо | Два последовательных serial full Headless run завершились `36/36` и `36/36`, оба exit 0 без post-test crash; raw console/exit + TRX/HTML сохранены отдельно | `artifacts/test-results/headless-storage-lifecycle/full-1-retained`, `full-2-retained` |
| EXEC | Закрыть full validation и review findings | 1.00 | PR checks/merge | Commit/push и открыть draft PR | Нет | Не применимо | Full Unit `830/830` exit 0; final solution build exit 0/0 errors; format verify и diff/scope PASS; architecture/QA PASS; governance evidence findings исправлены | `unit-full-retained`, `build-final-retained`, Post-EXEC review verdicts |
| EXEC | Завершить local Post-EXEC re-review | 1.00 | HSL-AC-05 delivery | Зафиксировать exact scope и открыть draft PR | Нет | Не применимо | Architecture, QA и delivery/governance independent re-reviews вернули PASS; remaining actionable findings отсутствуют | Final reviewer verdicts, эта spec |
