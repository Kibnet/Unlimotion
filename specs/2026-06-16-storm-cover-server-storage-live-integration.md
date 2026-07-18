# STORM /storm:cover: live RavenDB/SignalR integration harness для ST-0011

## 0. Метаданные
- Тип (профиль): `storm-product-development` + `delivery-task/QUEST`
- Владелец: product/engineering
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка Unlimotion
- Instruction stack:
  - `C:\Users\Kibnet\.codex\agents\AGENTS.md`
  - `instructions/governance/routing-matrix.md`
  - `instructions/core/model-behavior-baseline.md`
  - `instructions/core/quest-governance.md`
  - `instructions/core/quest-mode.md`
  - `instructions/core/testing-baseline.md`
  - `instructions/contexts/testing-dotnet.md`
  - `instructions/profiles/dotnet-backend-api.md`
  - `instructions/profiles/dotnet-ravendb.md`
  - `instructions/profiles/storm-product-development.md`
  - `instructions/governance/spec-linter.md`
  - `instructions/governance/spec-rubric.md`
  - `instructions/governance/review-loops.md`
  - локальный `AGENTS.override.md`
- Ограничения:
  - До явного утверждения SPEC не менять production code, tests, test annotations, STORM artifacts и поведение продукта.
  - После утверждения разрешены только изменения, прямо связанные с live RavenDB/SignalR coverage для `ST-0011`, `AC-0033`, `SC-0011-002`, `CV-0002`.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие артефакты.
  - Не заменять acceptance criteria на Gherkin.
  - Не откатывать уже добавленные `TS-0017`, `TS-0018`, security fix и BDD links.
  - Не менять test annotations без отдельного подтверждения.
  - UI tests не обязательны, пока не меняется UI-facing flow; если затрагивается UI, остановиться и расширить SPEC по локальному `AGENTS.override.md`.
  - Если stable live harness требует Docker, внешнюю сеть, длительный ручной server process или нестабильный RavenDB state, остановиться и оформить отдельную infrastructure SPEC.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/traceability.md`
  - `features/storm/st-0011-server-storage.feature`
  - `specs/2026-06-14-storm-cover-server-storage-bdd.md`
  - `specs/2026-06-16-storm-cover-server-storage-security-integration.md`

Если пользователь утверждает эту SPEC фразой `Спеку подтверждаю`, это разрешает перейти в EXEC и внести минимальные test/code/artifact changes в рамках этой спеки.

## 1. Overview / Цель
Цель: продолжить `/storm:cover` по `ST-0011` после закрытия `GetTask` user-scope и добавить stable live integration evidence для оставшегося gap: RavenDB-backed task API и SignalR delivery между клиентами должны быть проверены через реальный ASP.NET Core host, а не только source/contract tests.

Outcome contract:
- Success means:
  - Есть stable integration harness, который стартует server storage host в test process с изолированным RavenDB state.
  - Есть passing live test evidence для `SC-0011-002`: task change от одного authenticated клиента доставляется другому клиенту того же пользователя через SignalR и/или подтверждается RavenDB-backed API smoke.
  - `CV-0002` обновлён по фактическому evidence: `covered_by_live_integration_tests`, `critical_live_smoke_covered`, либо `blocked_by_infrastructure` с конкретной причиной.
  - `AC-0033` не повышается до `full`, если live tests не покрывают весь CRUD + delivery контракт; допустимое повышение до `critical` только при достаточном live smoke evidence.
  - Существующие `TS-0017` и `TS-0018` сохраняются.
- Итоговый артефакт / output:
  - новый live integration test или зафиксированный infrastructure blocker;
  - при необходимости test-only fixture/packages;
  - обновлённые `storm.json`, `bdd-sync.md`, `bdd-lint.md`, `coverage.md`, `traceability.md`;
  - обновлённая текущая SPEC с Post-EXEC review.
- Stop rules:
  - Не больше двух попыток стабилизировать live harness.
  - Остановиться, если RavenDB Embedded требует machine-specific license/config, которую нельзя изолировать в test fixture.
  - Остановиться, если SignalR live delivery требует изменения публичного client/server contract.
  - Остановиться, если fix выходит за минимальную test-hostability настройку и меняет auth model, DTO, route, storage schema или UI flow.

## 2. Текущее состояние (AS-IS)
- `ST-0011` описывает optional server storage с authentication и real-time updates.
- `AC-0032` имеет `critical` coverage через `TS-0017`.
- `AC-0033` остаётся `partial`: `TS-0017` и `TS-0018` покрывают authenticated endpoint contracts, `GetAll/BulkInsert/GetTask` user-scope contract и SignalR handler mapping, но live RavenDB/SignalR integration остаётся gap.
- `SC-0011-002` имеет status `passing` и `automation_status = passing_contract_and_security_regression_coverage`, но это не live integration evidence.
- `CV-0002` сейчас классифицирован как `security_user_scope_covered_live_integration_gap_remaining`.
- `src/Unlimotion.Server/Program.cs` exposes `Program.CreateWebHostBuilder(args)`, что даёт потенциальный entrypoint для ASP.NET Core test host.
- `src/Unlimotion.Server/Startup.cs` регистрирует `AddRavenDbServices()`, `AddSignalR()`, `AppHost` и maps `ChatHub` на `/chathub`.
- `src/Unlimotion.Server/StartupExtensions.cs` стартует `Raven.Embedded.EmbeddedServer.Instance`, создаёт `IDocumentStore` и session services. Глобальный embedded server instance создаёт риск test isolation.
- `src/Unlimotion.Server/hubs/ChatHub.cs` использует реальные SignalR groups `User_{uid}` и отправляет `ReceiveTaskItem` через `Clients.GroupExcept`, поэтому positive delivery test требует минимум два SignalR clients.
- `src/Unlimotion/ServerStorage.cs` подключается к `/ChatHub`, вызывает auth/login и мапит `ReceiveTaskItem`/`DeleteTaskItem` в `Updating`.
- В `src/Unlimotion.Test` нет текущего `WebApplicationFactory`, `TestServer`, `RavenTestDriver` или stable server storage fixture.
- В `src/Directory.Packages.props` нет явных test-host packages для ASP.NET Core integration tests.
- В отчётах может остаться устаревшая формулировка, где `GetTask user-scope` всё ещё указан как follow-up; EXEC должен не возобновлять этот gap, а синхронизировать отчёты с `TS-0018` evidence, если эти отчёты затрагиваются.

## 3. Проблема
Корневая проблема: `SC-0011-002` подтверждён contract/security tests, но продуктовый контракт говорит о live ServiceStack/RavenDB endpoints и SignalR delivery между клиентами. Без stable live evidence `/storm:cover` не может честно закрыть `CV-0002` и повысить доверие к `AC-0033`.

## 4. Цели дизайна
- Разделение ответственности: test host fixture, live сценарии и STORM artifact sync отделены.
- Повторное использование: использовать `Program.CreateWebHostBuilder`, `Startup`, реальные `ChatHub`, `TaskService` и текущий TUnit workflow.
- Тестируемость: harness должен быть deterministic, isolated, short-running и запускаться через `--treenode-filter`.
- Консистентность: сохранить trace chain `Story -> AC -> Scenario -> Test -> Code`.
- Обратная совместимость: не менять product route, DTO, auth token flow, storage schema и UI.

## 5. Non-Goals (чего НЕ делаем)
- Не строим полноценный executable Gherkin runner.
- Не меняем `step_definitions` на real runner без отдельного `/storm:bdd-implement`.
- Не закрываем Telegram `ST-0014`, platform shells `ST-0015` и unrelated coverage backlog.
- Не добавляем Docker/RavenDB external service requirement.
- Не меняем production behavior для успешного local storage, Git backup или Settings UI.
- Не переписываем RavenDB registration architecture шире, чем нужно для test-hostability.
- Не удаляем и не пересоздаём существующие STORM stories/tests/conflicts/dependencies.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` -> live integration tests для `SC-0011-002`.
- `src/Unlimotion.Test/ServerStorageLiveIntegrationFixture.cs` или nested fixture -> start/stop test host, test users, SignalR clients, temp RavenDB directories.
- `src/Unlimotion.Test/Unlimotion.Test.csproj` -> test-only package references, если нужен `Microsoft.AspNetCore.TestHost` или `Microsoft.AspNetCore.Mvc.Testing`.
- `src/Directory.Packages.props` -> central package version для test-host packages, только если package добавляется.
- `src/Unlimotion.Server/StartupExtensions.cs` или `Startup.cs` -> только минимальная test-hostability seam, если без неё нельзя изолировать RavenDB state.
- `docs/product/storm.json` -> добавить новый test evidence (`TS-0019`, если test добавлен) и обновить `CV-0002`.
- `features/storm/st-0011-server-storage.feature` -> добавить `@test:TS-0019` к `SC-0011-002` только после successful run.
- `docs/product/reports/*` -> синхронизировать coverage, BDD sync/lint и traceability по фактическому evidence.
- `specs/2026-06-16-storm-cover-server-storage-live-integration.md` -> EXEC journal и Post-EXEC review.

### 6.2 Детальный дизайн
- Предпочтительный harness path:
  1. `WebApplicationFactory<Program>` или `TestServer` на базе `Program.CreateWebHostBuilder`, потому что это запускает ASP.NET Core host in-process без внешнего порта.
  2. Для SignalR client использовать `HttpMessageHandlerFactory`/test server handler, если transport стабилен.
  3. Если SignalR over TestServer нестабилен или unsupported, перейти к ephemeral Kestrel localhost port с автоматическим выбором свободного порта.
  4. Если оба path нестабильны за две попытки, остановиться с `blocked_by_infrastructure`.
- RavenDB isolation:
  - использовать test-specific content root/temp directories для `RavenDb:ServerOptions`;
  - не писать в production/user data directories;
  - не полагаться на shared RavenDB state между tests;
  - clean up temp directories после run, если embedded server отпускает locks.
- Auth setup:
  - использовать реальные auth endpoints или прямой ServiceStack auth setup только через public/test-host path;
  - test config должен выставлять development/test security options без изменения production defaults;
  - bearer token передавать SignalR client как в `ServerStorage`.
- Live scenarios:
  - `ServerStorage_LiveSignalR_SaveTask_DeliversUpdateToSecondClientForSameUser`: два authenticated connections для одного user; client A вызывает save; client B получает `ReceiveTaskItem`.
  - `ServerStorage_LiveSignalR_SaveTask_DoesNotEchoToSender`: sender не получает self-echo из-за `GroupExcept`, если это можно проверить без race.
  - `ServerStorage_LiveRaven_TaskApi_RoundTripsAuthenticatedUserTasks`: authenticated `BulkInsert/GetAll/Load` smoke подтверждает RavenDB-backed endpoint path.
  - `ServerStorage_LiveSignalR_DeleteTask_DeliversRemoveToSecondClient`: optional, добавлять только если save delivery already stable.
- Output contract / evidence rules:
  - `passing`/coverage upgrade ставится только после actual targeted test run.
  - Если test flaky, не связывать его как passing evidence.
  - Если harness blocked, reports должны оставить `CV-0002` как live integration blocker и указать concrete failure reason.
- Visual planning artifact для UI-facing изменений: `Не применимо`, UI не меняется.
- UI test video evidence для UI automation задач: `Не применимо`, UI automation не затрагивается.
- Границы сохранения поведения:
  - допустимы только test-only packages, test fixture, artifact sync и минимальная test-hostability seam;
  - недопустимы изменения route `/tasks/{Id}`, `/chathub`, DTO shape, token semantics, SignalR group naming и persisted task model без отдельной SPEC.
- Обработка ошибок:
  - test должен иметь bounded timeouts для SignalR receive;
  - timeout считается failed evidence, а не reason silently pass;
  - host startup errors фиксируются в Post-EXEC review.
- Производительность:
  - targeted live suite должна быть short-running;
  - no unbounded retry loops;
  - full test run может быть next-best evidence, если известные unrelated headless flakes повторяются.

## 7. Бизнес-правила / Алгоритмы
- Authenticated task API работает только для текущего `session.UserAuthId`.
- Live SignalR delivery для `SC-0011-002` означает: изменение задачи, инициированное одним подключением user group, observable у другого подключения того же пользователя.
- `GroupExcept` означает, что отправитель не должен получать свой же `ReceiveTaskItem` в рамках этого hub call.
- Cross-user non-delivery не является обязательным для первого live smoke, но должно быть добавлено, если harness уже стабилен без существенного расширения.
- Coverage нельзя повышать выше фактически проверенного уровня.

## 8. Точки интеграции и триггеры
- `Program.CreateWebHostBuilder(args)` -> test host entrypoint.
- `Startup.ConfigureServices` -> RavenDB, SignalR, ServiceStack/AppHost registration.
- `Startup.Configure` -> SignalR endpoint `/chathub` and ServiceStack middleware.
- `AuthService` -> login/register/refresh endpoints.
- `TaskService` -> `GetAllTasks`, `GetTask`, `BulkInsertTasks`.
- `ChatHub.Login`, `ChatHub.SaveTask`, `ChatHub.DeleteTasks` -> live SignalR scenario.
- `ServerStorage` или direct SignalR/ServiceStack clients -> test client path.
- `storm.json` and reports -> evidence sync after validation.

## 9. Изменения модели данных / состояния
- Production persisted model: не меняется.
- RavenDB database/indexes: не менять без separate SPEC; test database может быть ephemeral.
- Test state: temp users/tasks/tokens создаются внутри fixture и изолируются.
- STORM state: допускается новый `TS-0019`, links/status/coverage notes для `AC-0033`, `SC-0011-002`, `CV-0002`.
- `step_definitions`: не менять, если не вводится executable BDD runner.

## 10. Миграция / Rollout / Rollback
- Миграция production данных: не применимо.
- Rollout: обычный PR с tests, optional test-host package/fixture и artifact sync.
- Rollback: revert new test/fixture/package/artifact changes; если был минимальный production test-hostability seam, revert его отдельно.
- Backward compatibility: production server behavior не должен измениться, кроме already-approved `GetTask` user isolation из предыдущей SPEC.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `AC-0033`: есть live RavenDB-backed task API smoke и/или live SignalR delivery evidence для authenticated clients.
  - `SC-0011-002`: связан с новым live integration test только после successful targeted run.
  - `CV-0002`: статус и remaining gap обновлены строго по evidence.
  - Existing `TS-0017/TS-0018` сохраняются и не деградируют.
- Какие тесты добавить/изменить:
  - `ServerStorage_LiveRaven_TaskApi_RoundTripsAuthenticatedUserTasks`
  - `ServerStorage_LiveSignalR_SaveTask_DeliversUpdateToSecondClientForSameUser`
  - optional: `ServerStorage_LiveSignalR_SaveTask_DoesNotEchoToSender`
  - optional: `ServerStorage_LiveSignalR_DoesNotDeliverTaskChangeToOtherUser`
  - optional: `ServerStorage_LiveSignalR_DeleteTask_DeliversRemoveToSecondClient`
- Characterization tests / contract checks:
  - сохранить `ServerStorageBddContractTests` как baseline;
  - если live test требует production seam, сначала добавить failing/blocked evidence в SPEC journal, затем минимальный change.
- Visual acceptance для UI-facing изменений: `Не применимо`.
- UI video evidence для UI-facing фич/багфиксов: `Не применимо`.
- Базовые замеры до/после для performance tradeoff: `Не применимо`; live tests должны оставаться bounded and short-running.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed`
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --maximum-parallel-tests 1 --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
- Stop rules для test/retrieval/tool/validation loops:
  - Если target live test проходит изолированно, но full test suite падает на known unrelated Avalonia.Headless teardown, проверить failed tests isolated и зафиксировать residual unrelated risk.
  - Если package restore/build требует unavailable workload unrelated to test project, использовать test project build/test as next-best evidence.
  - Если live harness не стабилизирован за две попытки, не добавлять flaky test и завершить с artifact-only blocker update.

## 12. Риски и edge cases
- Риск: `Raven.Embedded.EmbeddedServer.Instance` глобален и может оставлять locked state между tests. Смягчение: один fixture, temp paths, sequential targeted run, bounded cleanup.
- Риск: SignalR over `TestServer` может отличаться от Kestrel/WebSocket behavior. Смягчение: prefer TestServer first, fallback to ephemeral Kestrel if needed.
- Риск: startup config depends on local `appsettings.json`, secrets or RavenDB license. Смягчение: test config overrides and stop rule if machine-specific secret is required.
- Риск: `/ChatHub` client URL differs in casing from `/chathub` server map. Смягчение: live test will reveal; do not change route unless failing evidence proves case-sensitive deployment issue.
- Риск: adding integration packages increases dependency surface. Смягчение: test-only package refs through central package management, no production package changes.
- Риск: full suite flakiness masks current change. Смягчение: targeted evidence first, isolated reruns for unrelated failures.

## 13. План выполнения
1. Зафиксировать EXEC start: `git status --short`, approved SPEC и текущий `CV-0002` state.
2. Проверить fastest host path: can `WebApplicationFactory<Program>`/`TestServer` build and start with test config?
3. Если нужен test-host package, добавить его только в test project/central package props.
4. Сделать fixture с isolated RavenDB temp state and bounded disposal.
5. Добавить минимальный live RavenDB API smoke test.
6. Добавить live SignalR two-client save delivery test.
7. Запустить targeted live tests.
8. Если harness нестабилен, выполнить один fallback attempt через ephemeral Kestrel; если снова нестабилен, остановиться и обновить artifacts as blocker.
9. При passing evidence обновить `storm.json`, feature tags and reports (`/storm:bdd-sync`, `/storm:bdd-lint`, coverage).
10. Запустить baseline `ServerStorageBddContractTests`, test project build, full test project run or isolated fallback, STORM validator and `git diff --check`.
11. Выполнить Post-EXEC review.

## 14. Открытые вопросы
- Блокирующих вопросов нет.
- Неблокирующий риск для EXEC: точный test host package/version выбирается по существующему .NET 10/package management state в репозитории, а не угадывается в SPEC.
- Неблокирующий риск для EXEC: если RavenDB Embedded не может быть изолирован без machine-specific setup, эта SPEC должна завершиться blocker report, а не flaky test.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `dotnet-backend-api`, `dotnet-ravendb`, `testing-dotnet`.
- Выполненные требования профиля:
  - `/storm:cover` с code/test changes идёт через QUEST.
  - Existing STORM artifacts сохраняются, `/storm:full-cycle` не запускается.
  - Acceptance criteria не заменяются Gherkin.
  - Scenario/Test/coverage status меняется только по actual evidence.
  - RavenDB integration получает isolation/rollback/stop rules.
  - TUnit validation использует `--treenode-filter`, а не VSTest `--filter`.
  - UI override учтён: UI не меняется; при UI change надо остановиться.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` | Новый live integration test class | Live evidence для `SC-0011-002` |
| `src/Unlimotion.Test/ServerStorageLiveIntegrationFixture.cs` | Fixture для test host/RavenDB/SignalR clients, если нужен отдельный файл | Изоляция host lifecycle и temp state |
| `src/Unlimotion.Test/Unlimotion.Test.csproj` | Test-only package refs, если нужен TestServer/WebApplicationFactory | Запустить ASP.NET Core host in-process |
| `src/Directory.Packages.props` | Central package version для test-host packages, если добавляются | Соблюсти central package management |
| `src/Unlimotion.Server/StartupExtensions.cs` | Только минимальный test-hostability seam, если без него live harness невозможен | Изолировать RavenDB test state без изменения product contract |
| `docs/product/storm.json` | Новый `TS-0019`/links/status/coverage по evidence | Синхронизировать STORM trace |
| `features/storm/st-0011-server-storage.feature` | Добавить `@test:TS-0019` к `SC-0011-002` по evidence | Синхронизировать Gherkin layer |
| `docs/product/reports/bdd-sync.md` | Обновить Scenario -> Test sync | Отразить live evidence или blocker |
| `docs/product/reports/bdd-lint.md` | Обновить warnings/gaps | Не скрывать harness blocker |
| `docs/product/reports/coverage.md` | Обновить `CV-0002` and behavior metrics | Продолжение `/storm:cover` |
| `docs/product/reports/traceability.md` | Обновить Story/AC/Scenario/Test/Code trace | Аудит требований |
| `specs/2026-06-16-storm-cover-server-storage-live-integration.md` | EXEC journal and Post-EXEC review | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `AC-0033` | `partial`, tests `TS-0017`, `TS-0018`, live gap | `critical` or still `partial` based on live evidence |
| `SC-0011-002` | `passing_contract_and_security_regression_coverage` | `passing_live_integration_coverage` or explicit `blocked_by_infrastructure` note |
| `CV-0002` | `security_user_scope_covered_live_integration_gap_remaining` | `covered_by_live_integration_tests`, `critical_live_smoke_covered`, or `blocked_by_infrastructure` |
| `ST-0011` | behavior coverage level 3 with live gap | behavior coverage level updated only if live test passes |

## 18. Альтернативы и компромиссы
- Вариант: перейти к `ST-0014` Telegram bot вместо live server storage.
  - Плюсы: следующий ranked gap тоже значим.
  - Минусы: `CV-0002` остаётся незавершённым после двух ST-0011 инкрементов.
  - Почему не выбран: пользователь сказал двигаться дальше после рекомендации live RavenDB/SignalR SPEC.
- Вариант: оставить live integration как manual gap без попытки harness.
  - Плюсы: нет риска flaky tests.
  - Минусы: `/storm:cover` не улучшает фактическую уверенность в real-time server storage.
  - Почему не выбран: есть server `Program.CreateWebHostBuilder`, поэтому разумно проверить stable host path.
- Вариант: full external RavenDB/Kestrel environment.
  - Плюсы: ближе к production deployment.
  - Минусы: дорого, нестабильно, требует внешних предпосылок.
  - Почему не выбран: текущая цель - repo-local deterministic evidence.
- Вариант: TestServer/WebApplicationFactory first.
  - Плюсы: no external port, in-process lifecycle, easy CI fit.
  - Минусы: SignalR transport может отличаться.
  - Почему выбран первым: минимальная инфраструктурная стоимость; fallback на ephemeral Kestrel сохраняет production-like path.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals и non-goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Есть TestServer-first дизайн, fallback, integration points, rollback и data isolation. |
| C. Безопасность изменений | 11-13 | PASS | Stop rules запрещают flaky/external harness и contract drift. |
| D. Проверяемость | 14-16 | PASS | Acceptance, tests, commands and planned files указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План пошаговый; package/version uncertainty не блокирует EXEC. |
| F. Соответствие профилю | 20 | PASS | STORM, QUEST, RavenDB and TUnit requirements отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope ограничен live coverage для `ST-0011/AC-0033/SC-0011-002/CV-0002`. |
| 2. Понимание текущего состояния | 5 | Зафиксированы `TS-0017/TS-0018`, `Program`, `Startup`, RavenDB, ChatHub and missing harness. |
| 3. Конкретность целевого дизайна | 5 | Указаны host strategy, fallback, tests, artifacts and stop rules. |
| 4. Безопасность (миграция, откат) | 5 | Нет data migration; rollback понятен; external infra запрещена. |
| 5. Тестируемость | 5 | Targeted TUnit commands and bounded live test strategy заданы. |
| 6. Готовность к автономной реализации | 5 | EXEC может идти автономно до passing evidence или explicit blocker. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-storm-cover-server-storage-live-integration.md`, central stack, local override, canonical template, spec-linter/rubric/review-loop docs, `docs/product/storm.json`, `docs/product/reports/coverage.md`, `features/storm/st-0011-server-storage.feature`, prior ST-0011 specs, `Program.cs`, `Startup.cs`, server csproj.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: PASS, spec path and planned files are explicit; no code/test/artifact changes made in SPEC.
  - Contract pass: PASS, scope preserves existing AC/Gherkin/tests and focuses only on live gap.
  - Adversarial risk pass: PASS, RavenDB global instance, SignalR transport, local secrets, stale artifact wording and full-suite flakiness are covered.
  - Re-review after fixes / Fix and re-review: not required; no blocker findings with required spec rewrite.
  - Stop decision: wait for `Спеку подтверждаю`.
- Evidence inspected:
  - Previous ST-0011 SPECs and Post-EXEC reviews.
  - `docs/product/storm.json` links/status for `AC-0033`, `SC-0011-002`, `TS-0017`, `TS-0018`, `CV-0002`.
  - `docs/product/reports/coverage.md` remaining gap and recommended next step.
  - `src/Unlimotion.Server/Program.cs` host builder entrypoint.
  - `src/Unlimotion.Server/Startup.cs` SignalR/ServiceStack/RavenDB registration.
  - `src/Unlimotion.Server/Unlimotion.Server.csproj` package/project structure.
- Depth checklist:
  - Scope drift / unrelated changes: planned scope excludes `ST-0014`, `ST-0015`, UI and Gherkin runner work.
  - Acceptance criteria: `AC-0033` live API/SignalR evidence is the target.
  - Validation evidence: commands cover targeted live tests, baseline tests, build, full test fallback, STORM validator and whitespace.
  - Unsupported claims: spec does not promise live coverage if harness is unstable.
  - Regression / edge case: RavenDB isolation, SignalR sender echo, cross-user delivery, URL casing and startup secrets considered.
  - Comments/docs/changelog: changelog not required; reports update only after evidence.
  - Hidden contract change: route, DTO, auth, storage schema and SignalR group semantics are protected.
  - Manual-review challenge: reviewer could ask why not skip to Telegram; spec justifies completing `CV-0002` first and has stop rule for unstable harness.
- No-findings justification: SPEC defines one preferred implementation path with fallback and explicit blocker handling; it does not authorize broad production redesign.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Live harness может оказаться невозможным из-за RavenDB Embedded/test host limitations | Use two-attempt stop rule and update artifacts as blocker instead of adding flaky tests | accepted-risk |

- Fixed before continuing: не применимо.
- Checks rerun: central stack/template/governance docs and selected repo files inspected.
- Needs human: требуется `Спеку подтверждаю`.
- Residual risks / follow-ups: if harness is blocked, next SPEC should be infrastructure-focused; if harness passes, next `/storm:cover` candidate is `ST-0014`.

### Post-EXEC Review
- Статус: PASS_WITH_RESIDUAL_RISK
- Scope reviewed: `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`, `src/Unlimotion.Test/Unlimotion.Test.csproj`, `src/Unlimotion.Server/AppModelMapping.cs`, `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, `docs/product/reports/*`, текущая SPEC.
- Decision: EXEC выполнен в рамках утверждённой SPEC; `SC-0011-002` получил passing live SignalR/RavenDB evidence через `TS-0019`, но production ServiceStack task API live smoke оставлен как отдельный gap.
- Review passes:
  - Scope/Evidence pass: PASS, изменения ограничены live server-storage coverage и STORM sync.
  - Contract pass: PASS, acceptance criteria сохранены отдельно от Gherkin; test annotations не менялись.
  - Adversarial risk pass: PASS_WITH_RESIDUAL_RISK, production ServiceStack AppHost live smoke упирается в trial/free-quota side effects; flaky/external harness не добавлен.
  - Re-review after fixes / Fix and re-review: PASS, временные изменения `ServiceStackKey.cs` и `appsettings.json` очищены; tracked diff не содержит этих файлов.
  - Stop decision: stop after scoped validation; для full coverage нужна отдельная SPEC.
- Evidence inspected:
  - `TS-0019` starts repo-local Kestrel host with isolated RavenDB directories and database name.
  - `TS-0019` uses real `ChatHub`, `AddRavenDbServices`, JWT auth provider setup and two SignalR clients.
  - `TS-0019` verifies sender `SaveTask` delivers `ReceiveTaskItem` to second authenticated client in same user group and does not echo to sender.
  - Production ServiceStack API live-smoke attempt was not retained because production AppHost startup hit ServiceStack trial/free-quota side effects.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, no UI, Telegram, platform-shell, runner or test annotation changes.
  - Acceptance criteria: PASS, `AC-0033` raised from partial to critical, not full.
  - Validation evidence: PASS for build, targeted contract tests, live test, STORM validator and diff check; full project run timed out.
  - Unsupported claims: PASS, reports explicitly keep ServiceStack API live-smoke as gap.
  - Regression / edge case: PASS, live test covers receiver delivery and sender non-echo; cross-user and delete delivery remain optional future scope.
  - Comments/docs/changelog: PASS, product artifacts and reports updated in Russian.
  - Hidden contract change: PASS, no route, DTO, auth, storage schema or UI behavior change intended.
  - Manual-review challenge: Reviewer may ask why `AC-0033` is not full; answer: ServiceStack API endpoints are not live-smoked in stable licensed/test-host path.
- No-findings justification: Scoped live SignalR/RavenDB behavior is verified by a passing integration test and artifacts match evidence; residual risks are explicitly carried forward.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | coverage | Production ServiceStack task API live smoke не покрыт стабильным harness из-за trial/free-quota side effects | Оформить отдельную SPEC для licensed/test-host strategy, если нужен full coverage `AC-0033` | open |
| LOW | validation | Full `Unlimotion.Test` sequential run timed out after 10 minutes without final summary | Использовать scoped passing evidence для текущего server-storage delivery; full-suite timeout требует отдельного triage при необходимости | accepted-risk |

- Fixed before final report:
  - `AppModelMapping` получил явные ignores для computed task members, чтобы real host startup проходил AutoMapper validation.
  - Временные изменения `ServiceStackKey.cs` и `appsettings.json` очищены; content diff отсутствует.
- Checks rerun:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore` -> passed with existing warnings.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed` -> passed 7/7.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed` -> passed 1/1.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` -> OK, 0 errors, 0 warnings.
  - `git diff --check` -> passed, no whitespace errors.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --maximum-parallel-tests 1 --output Detailed` -> timed out after 10 minutes without final summary; PID was stopped.
- Validation evidence: targeted server-storage scope green; STORM metrics now show `acceptance_criteria_good_coverage_ratio: 40/43`.
- Unrelated changes: previous `TaskService.GetTask` security follow-up and existing STORM artifact files preserved.
- Needs human: требуется отдельное подтверждение только для следующей SPEC.
- Residual risks / follow-ups: ServiceStack API live smoke, cross-user SignalR non-delivery and delete delivery can be covered in follow-up if product requires full server-storage coverage.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создать follow-up SPEC для live RavenDB/SignalR coverage `ST-0011` | 0.86 | Feasibility of TestServer/RavenDB/SignalR harness confirms only in EXEC | Запросить `Спеку подтверждаю` | Да | Нет | Пользователь сказал "Двигаемся дальше" после завершения security follow-up; next recommended step is live integration harness, but QUEST requires SPEC gate first | `specs/2026-06-16-storm-cover-server-storage-live-integration.md` |
| EXEC | Добавить stable live SignalR/RavenDB integration evidence для `SC-0011-002` | 0.84 | Production ServiceStack API live smoke needs separate licensed/test-host strategy | Обновить STORM artifacts и выполнить validations | Нет | Да, пользователь подтвердил SPEC фразой `Спеку подтверждаю` | Production AppHost ServiceStack path оказался license/quota-sensitive, поэтому выбран minimal AppHost с real ChatHub/RavenDB для stable delivery evidence | `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`, `src/Unlimotion.Test/Unlimotion.Test.csproj`, `src/Unlimotion.Server/AppModelMapping.cs` |
| EXEC | Синхронизировать `/storm:bdd-sync`, `/storm:bdd-lint` и coverage после `TS-0019` | 0.88 | Full suite timed out, но targeted evidence green | Завершить Post-EXEC review и отдать финал | Нет | Нет | `AC-0033` поднят только до critical: live SignalR covered, production ServiceStack API smoke remains gap | `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, `docs/product/reports/*.md` |
