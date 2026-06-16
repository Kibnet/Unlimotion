# STORM /storm:cover: ServiceStack task API live smoke для ST-0011

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
  - После утверждения разрешены только изменения, прямо связанные с ServiceStack task API live smoke для `ST-0011`, `AC-0033`, `SC-0011-002`, `CV-0002`.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие артефакты.
  - Не заменять acceptance criteria на Gherkin.
  - Не удалять существующие `TS-0017`, `TS-0018`, `TS-0019`, stories, tests, conflicts, dependencies и reports.
  - Не менять test annotations без отдельного подтверждения.
  - Не менять `ServiceStackKey`, production license flow, `appsettings.json` secrets, public routes или DTO shape без отдельной SPEC.
  - UI tests не обязательны, пока не меняется UI-facing behavior; если затрагивается UI, остановиться и расширить SPEC по локальному `AGENTS.override.md`.
  - Если stable ServiceStack API harness требует внешнюю сеть, ручной server process, production license mutation, route change или ServiceStack paid license, остановиться и обновить artifacts as blocker.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/traceability.md`
  - `features/storm/st-0011-server-storage.feature`
  - `specs/2026-06-16-storm-cover-server-storage-live-integration.md`
  - `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`
  - `src/Unlimotion.Server.ServiceInterface/TaskService.cs`
  - `src/Unlimotion.Server/AppHost.cs`
  - `src/Unlimotion.Server/Startup.cs`

Если пользователь утверждает эту SPEC фразой `Спеку подтверждаю`, это разрешает перейти в EXEC и внести минимальные test/code/artifact changes в рамках этой спеки.

## 1. Overview / Цель
Цель: продолжить `/storm:cover` по `ST-0011` после `TS-0019` и закрыть следующий конкретный gap `CV-0002`: проверить live ServiceStack task API path через HTTP ServiceStack pipeline, authenticated request, real RavenDB services and real `TaskService`, не полагаясь только на source/contract tests.

Outcome contract:
- Success means:
  - Есть stable test-only live harness, который стартует repo-local ASP.NET Core/Kestrel host с ServiceStack routing для `TaskService`, isolated RavenDB state и JWT auth.
  - Есть passing live test evidence для `BulkInsertTasks`, `GetAllTasks` и `GetTask` через `JsonServiceClient` или эквивалентный ServiceStack client.
  - Test подтверждает authenticated user-scope: задачи текущего пользователя возвращаются, задачи другого пользователя не протекают через `GetAll` и `GetTask`.
  - `CV-0002`, `AC-0033`, `SC-0011-002`, feature tags и reports синхронизированы по фактическому evidence.
  - `AC-0033` повышается до `full` только если live evidence действительно покрывает весь текущий supported server-storage contract; иначе остаётся `critical` с более узким remaining gap.
- Итоговый артефакт / output:
  - новый live ServiceStack task API test (`TS-0020`, если test добавлен) или зафиксированный blocker;
  - при необходимости test-only fixture changes;
  - обновлённые `storm.json`, `.feature` tag, `bdd-sync.md`, `bdd-lint.md`, `coverage.md`, `traceability.md`;
  - обновлённая текущая SPEC с Post-EXEC review.
- Stop rules:
  - Не больше двух попыток стабилизировать ServiceStack API harness.
  - Остановиться, если test требует изменения `ServiceStackKey`, production license registration, route attributes, DTO shape, auth token semantics, persisted task schema или UI flow.
  - Остановиться, если ServiceStack operation quota/licensing снова блокирует harness даже без production `AppHost` metadata plugins.
  - Остановиться, если duplicate route `/tasks` (`GetTasks` и `GetAllTasks`) требует публичного route redesign.

## 2. Текущее состояние (AS-IS)
- `ST-0011` описывает optional server storage с authentication и real-time updates.
- `AC-0033` сейчас `critical`: `TS-0017` покрывает contract-level server-storage behavior, `TS-0018` покрывает `GetTask` user-scope source regression, `TS-0019` покрывает live SignalR/RavenDB delivery.
- `SC-0011-002` имеет status `passing` и связан с `TS-0017`, `TS-0018`, `TS-0019`.
- `CV-0002` сейчас `critical_live_signalr_covered_service_stack_api_gap_remaining`: live SignalR covered, production ServiceStack task API live smoke остаётся gap.
- `TaskService` exposes:
  - `[Authenticate] Get(GetAllTasks)` -> queries `TaskItem` by `UserId`.
  - `[Authenticate] GetAsync(GetTask)` -> decodes `Id` and queries by `Id && UserId`.
  - `[Authenticate] Post(BulkInsertTasks)` -> maps molds, sets `UserId`, prefixes `TaskItem/`, writes via RavenDB `BulkInsert`.
- DTO routes:
  - `GET /tasks` for `GetAllTasks` and also `GetTasks`, creating a possible route ambiguity.
  - `GET /tasks/{Id}` for `GetTask`, with possible encoded slash risk for IDs like `TaskItem/<id>`.
  - `POST /tasks/bulk` for `BulkInsertTasks`.
- `AppHost` scans `typeof(AuthService).Assembly`, configures `ValidationFeature`, `PostmanFeature`, `CorsFeature`, `AuthFeature`, `OpenApiFeature`.
- Production `Startup.Configure` calls `new ServiceStackKey().Register(Configuration)` before `app.UseServiceStack(host)`.
- Prior live-harness attempt using production AppHost startup hit ServiceStack trial/free-quota side effects; the retained stable `TS-0019` uses a minimal test AppHost that does not scan production service routes.
- Existing live fixture already starts isolated RavenDB directories/database and can generate JWT for test users.
- Full `Unlimotion.Test` sequential run timed out after 10 minutes in the previous delivery; targeted server-storage tests passed.

## 3. Проблема
Корневая проблема: `/storm:cover` still lacks live evidence that authenticated ServiceStack task endpoints work through a real HTTP ServiceStack pipeline with RavenDB and user-scope isolation. Without this, `AC-0033` cannot be honestly considered full, even after SignalR live delivery is covered.

## 4. Цели дизайна
- Разделение ответственности: ServiceStack API harness, task API scenarios and artifact sync are separate.
- Повторное использование: reuse existing live RavenDB setup and token-generation pattern from `TS-0019`.
- Тестируемость: deterministic, repo-local, bounded tests with isolated database and no external ServiceStack account/network dependency.
- Консистентность: preserve trace chain `Story -> AC -> Scenario -> Test -> Code`.
- Обратная совместимость: no public route, DTO, auth, RavenDB schema, SignalR or UI behavior changes.

## 5. Non-Goals (чего НЕ делаем)
- Не строим full production deployment smoke with paid ServiceStack license.
- Не меняем production `ServiceStackKey` и не обновляем `appsettings.json` license key.
- Не меняем route attributes для `GetTasks`, `GetAllTasks`, `GetTask` и `BulkInsertTasks`.
- Не меняем `TaskService` behavior, если live test reveals a route/encoding limitation; вместо этого фиксируем blocker and propose separate SPEC.
- Не расширяем SignalR coverage: `TS-0019` уже покрывает live delivery.
- Не закрываем `ST-0014`, `ST-0015`, notification UX, attachment workflow и unrelated backlog.
- Не добавляем executable Gherkin runner или step definitions.
- Не меняем UI и не добавляем UI tests.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` -> добавить ServiceStack task API live test(s) and reusable fixture methods.
- Test-only nested `LiveServiceStackTaskApiTestAppHost` -> ServiceStack AppHost that scans production service assembly and registers only the minimal plugins needed for JWT auth and task services.
- Existing live fixture -> temp content root, isolated RavenDB settings, Kestrel URL, JWT creation, `JsonServiceClient` helper.
- `docs/product/storm.json` -> add/update `TS-0020`, `AC-0033`, `SC-0011-002`, `CV-0002`, behavior metrics.
- `features/storm/st-0011-server-storage.feature` -> add `@test:TS-0020` to `SC-0011-002` only after passing evidence.
- `docs/product/reports/*` -> sync coverage, BDD sync/lint and traceability.
- Current SPEC -> EXEC journal and Post-EXEC review.

### 6.2 Детальный дизайн
- Preferred harness path:
  1. Extend the existing repo-local Kestrel/RavenDB test fixture.
  2. Use a test-only AppHost based on `AppHostBase("Unlimotion.LiveServiceStackTaskApiTest", typeof(TaskService).Assembly)`.
  3. Configure `AuthFeature` with `JwtAuthProvider` equivalent to `TS-0019` and no standard auth service routes.
  4. Register ServiceStack/Funq dependencies needed by `TaskService`: `IAsyncDocumentSession`, `IDocumentStore`, `IMapper`.
  5. Use `JsonServiceClient(BaseUrl)` with `BearerToken = accessToken`.
  6. Exercise `PostAsync(new BulkInsertTasks { ... })`, `GetAsync(new GetAllTasks())`, `GetAsync(new GetTask { Id = ... })`.
- Data flow:
  - Test creates two authenticated users/tokens in isolated RavenDB.
  - Client A posts task molds through ServiceStack task API.
  - Client A reads through `GetAllTasks` and `GetTask`.
  - Client B cannot observe Client A task through `GetAllTasks`; `GetTask` with Client A id must not return Client A task data.
- Output contract / evidence rules:
  - A scenario can be linked as passing evidence only after targeted test run passes.
  - If `GetTask` route cannot encode `TaskItem/<id>` without public route changes, do not claim full coverage; record route/encoding blocker.
  - If duplicate `GET /tasks` route selects `GetTasks` instead of `GetAllTasks`, do not change production route in this SPEC; record blocker.
  - If ServiceStack license/quota blocks service scan, do not mutate license flow; record blocker.
- Visual planning artifact для UI-facing изменений: `Не применимо`, UI не меняется.
- UI test video evidence для UI automation задач: `Не применимо`, UI automation не затрагивается.
- Границы сохранения поведения:
  - permitted: test-only AppHost/fixture/test additions; STORM artifact sync after evidence;
  - forbidden: public route changes, DTO contract changes, persisted schema changes, auth model changes, production license bootstrap changes.
- Обработка ошибок:
  - Test timeouts must be bounded.
  - Unauthorized/forbidden/not-found behavior for cross-user `GetTask` may be asserted as "does not return other user's task" if current API maps no-result to non-success.
  - Unexpected 500 caused by no-result can be accepted only as non-leak evidence, not as polished API behavior.
- Производительность:
  - Targeted ServiceStack live test should remain short-running and isolated.
  - No unbounded retries or external network calls.

## 7. Бизнес-правила / Алгоритмы
- Authenticated task API работает только для текущего `session.UserAuthId`.
- `BulkInsertTasks` assigns the authenticated `UserAuthId` to every stored task.
- `GetAllTasks` must return only current user's tasks.
- `GetTask` must not return a task owned by another user.
- Coverage level cannot be higher than actual live evidence:
  - `critical`: contract/security/live SignalR plus partial ServiceStack API smoke.
  - `full`: supported task API round-trip and live SignalR delivery are both covered without unresolved route/license blockers.

## 8. Точки интеграции и триггеры
- `TaskService.Post(BulkInsertTasks)` -> live insert path.
- `TaskService.Get(GetAllTasks)` -> live authenticated list path.
- `TaskService.GetAsync(GetTask)` -> live authenticated load path.
- `AuthFeature` / `JwtAuthProvider` -> authentication filter used by `[Authenticate]`.
- `AddRavenDbServices()` -> isolated embedded RavenDB test state.
- `JsonServiceClient` -> same ServiceStack client family as `ServerStorage`.
- `storm.json` and reports -> evidence sync after validation.

## 9. Изменения модели данных / состояния
- Production persisted model: не меняется.
- RavenDB indexes/schema: не меняются.
- Test state: temporary users/tasks/tokens in isolated RavenDB database.
- STORM state: допускается новый `TS-0020`, updated links/status/coverage notes for `AC-0033`, `SC-0011-002`, `CV-0002`.
- `step_definitions`: не меняются.

## 10. Миграция / Rollout / Rollback
- Миграция production данных: не применимо.
- Rollout: обычный PR/working branch with tests and artifacts.
- Rollback: revert new test/fixture changes and related artifact sync.
- Backward compatibility: public API routes, DTOs and storage schema remain unchanged.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `AC-0033`: ServiceStack task API live smoke proves authenticated `BulkInsert/GetAll/GetTask` over HTTP against real RavenDB services.
  - `SC-0011-002`: linked to new `TS-0020` only after passing targeted run.
  - `CV-0002`: status and remaining gap updated strictly by evidence.
  - Existing `TS-0017/TS-0018/TS-0019` remain linked and green.
- Какие тесты добавить/изменить:
  - `ServerStorage_LiveServiceStackTaskApi_BulkInsertGetAllAndGetTask_RoundTripsAuthenticatedUserTasks`.
  - optional if stable: `ServerStorage_LiveServiceStackTaskApi_DoesNotReturnOtherUsersTasks`.
- Characterization tests / contract checks:
  - preserve `ServerStorageBddContractTests`.
  - preserve `ServerStorage_LiveSignalR_SaveTask_DeliversUpdateToSecondClientForSameUser`.
  - if live API test exposes route/encoding limitation, stop with blocker instead of changing routes.
- Visual acceptance для UI-facing изменений: `Не применимо`.
- UI video evidence для UI-facing фич/багфиксов: `Не применимо`.
- Базовые замеры до/после для performance tradeoff: `Не применимо`; targeted live test duration should be reported.
- Команды для проверки:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --maximum-parallel-tests 1 --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
- Stop rules для test/retrieval/tool/validation loops:
  - If full test project run times out again, report timeout and use scoped passing evidence for current server-storage scope.
  - If ServiceStack API harness fails before executing task API due license/quota, stop and artifact-sync blocker.
  - If a route/encoding bug is found, do not fix it in this SPEC; create separate implementation SPEC.

## 12. Риски и edge cases
- Риск: ServiceStack scans too many operations and hits free quota. Смягчение: test-only minimal AppHost, no Postman/OpenApi/metadata plugins; stop if still blocked.
- Риск: ServiceStack dependency injection for `TaskService` does not resolve ASP.NET scoped RavenDB session. Смягчение: explicit test-only Funq registrations or scoped factory; stop if this requires production architecture change.
- Риск: duplicate `GET /tasks` route chooses `GetTasks` instead of `GetAllTasks`. Смягчение: capture blocker; route redesign requires separate SPEC.
- Риск: `GetTask` ID with slash requires URL encoding. Смягчение: test the actual ServiceStack client behavior; no route mutation in this SPEC.
- Риск: `BulkInsert` prefixes IDs, while client IDs may already contain `TaskItem/`. Смягчение: test with client-style unprefixed ID and read back stored prefixed ID.
- Риск: full suite timeout repeats. Смягчение: targeted evidence first and explicit residual risk.

## 13. План выполнения
1. Зафиксировать EXEC start: `git status --short`, current `CV-0002` state and approved SPEC.
2. Extend existing live fixture with `JsonServiceClient` helper and test-only ServiceStack task API AppHost.
3. Register minimal ServiceStack auth and `TaskService` dependencies without production license bootstrap.
4. Add live task API round-trip test.
5. Add cross-user non-leak assertion if stable within same test.
6. Run targeted live ServiceStack/SignalR test class.
7. If passing, update `storm.json`, feature tag and reports with `TS-0020`.
8. If blocked, update artifacts with concrete blocker and do not add flaky test as passing evidence.
9. Run baseline contract tests, build, STORM validator, `git diff --check`, and full test project run or documented timeout fallback.
10. Fill Post-EXEC review.

## 14. Открытые вопросы
- Блокирующих вопросов нет.
- Неблокирующий риск: exact ServiceStack DI bridge is determined in EXEC from actual runtime behavior.
- Неблокирующий риск: `AC-0033` full/critical decision depends on actual passing evidence and any remaining route/license gaps.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `dotnet-backend-api`, `dotnet-ravendb`, `testing-dotnet`.
- Выполненные требования профиля:
  - `/storm:cover` with tests/artifact changes goes through QUEST.
  - Existing STORM artifacts are preserved; `/storm:full-cycle` is not restarted.
  - Acceptance criteria are not replaced by Gherkin.
  - Scenario/Test/coverage status changes only after actual evidence.
  - RavenDB integration uses isolated state and rollback/stop rules.
  - TUnit validation uses `--treenode-filter`, not VSTest `--filter`.
  - UI override is accounted for: no UI-facing change.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` | Добавить live ServiceStack task API test and fixture helpers | Evidence for `AC-0033` ServiceStack API gap |
| `src/Unlimotion.Test/Unlimotion.Test.csproj` | Не ожидается; только если compile reference is missing | Test compile support |
| `docs/product/storm.json` | Add `TS-0020`, update links/status/coverage if evidence passes or blocker if not | STORM trace sync |
| `features/storm/st-0011-server-storage.feature` | Add `@test:TS-0020` to `SC-0011-002` only after passing run | Gherkin layer sync |
| `docs/product/reports/bdd-sync.md` | Update Scenario -> Test sync | Reflect actual evidence/blocker |
| `docs/product/reports/bdd-lint.md` | Update warnings/gaps | Reflect actual evidence/blocker |
| `docs/product/reports/coverage.md` | Update `CV-0002` and behavior metrics | Continue `/storm:cover` |
| `docs/product/reports/traceability.md` | Add ServiceStack task API trace | Audit chain |
| `docs/product/reports/ranking.md` | Optional status sync only if `CV-0002` status changes materially | Keep backlog honest |
| `specs/2026-06-16-storm-cover-server-storage-servicestack-api-live-smoke.md` | EXEC journal and Post-EXEC review | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `AC-0033` | `critical`, tests `TS-0017`, `TS-0018`, `TS-0019`; ServiceStack API live smoke gap | `full` only if ServiceStack API live smoke passes without unresolved gaps; otherwise `critical` with narrower blocker |
| `SC-0011-002` | passing contract/security/live SignalR evidence | passing contract/security/live SignalR + ServiceStack API evidence, or explicit blocker |
| `CV-0002` | `critical_live_signalr_covered_service_stack_api_gap_remaining` | `covered_by_live_task_api_and_signalr_tests`, `critical_with_route_or_license_gap`, or `blocked_by_servicestack_api_harness` |
| `ST-0011` | partial story with stronger server-storage evidence | partial or implemented only if product support and full API/delivery evidence justify it |

## 18. Альтернативы и компромиссы
- Вариант: Modify production `Startup`/`ServiceStackKey` to support test license bypass.
  - Плюсы: ближе к production startup.
  - Минусы: touches production license behavior and risks hiding operational contract changes.
  - Почему не выбран: current goal is API live smoke; license bootstrap needs separate design if required.
- Вариант: Use production AppHost exactly as-is.
  - Плюсы: maximum production fidelity.
  - Минусы: prior attempt hit trial/free-quota side effects before stable test evidence.
  - Почему не выбран первым: likely flaky/environment-bound; minimal AppHost still exercises ServiceStack HTTP routing, auth filter, DTO binding, `TaskService`, RavenDB and mapper.
- Вариант: Skip ServiceStack API live smoke and move to `ST-0014`.
  - Плюсы: avoids infrastructure complexity.
  - Минусы: leaves `CV-0002` as a known high-value server-storage gap.
  - Почему не выбран: пользователь сказал "делай" after recommendation to cover this specific gap.
- Вариант: Change routes to remove duplicate `/tasks`.
  - Плюсы: could improve API clarity.
  - Минусы: public API behavior change.
  - Почему не выбран: route redesign is outside this SPEC and needs separate approval.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals and non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | Test-only AppHost path, data flow, integration points and rollback defined. |
| C. Безопасность изменений | 11-13 | PASS | Production license/routes/DTO/schema changes forbidden; stop rules explicit. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, target tests, validation commands and changed files listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan, mappings, alternatives and risk controls are concrete. |
| F. Соответствие профилю | 20 | PASS | STORM, QUEST, .NET backend/RavenDB and TUnit rules covered. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope limited to `AC-0033` ServiceStack task API live smoke and artifact sync. |
| 2. Понимание текущего состояния | 5 | Captures `TS-0017/18/19`, current `CV-0002`, ServiceStack routes, license/quota risk and existing live fixture. |
| 3. Конкретность целевого дизайна | 5 | Preferred harness, test flow, dependencies and stop conditions are defined. |
| 4. Безопасность (миграция, откат) | 5 | No data migration; public API/license changes explicitly excluded. |
| 5. Тестируемость | 5 | Targeted commands and evidence rules are explicit. |
| 6. Готовность к автономной реализации | 5 | EXEC can proceed until passing evidence or concrete blocker without asking for design choices. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-storm-cover-server-storage-servicestack-api-live-smoke.md`, central stack, local override, canonical template, spec-linter/rubric/review-loop docs, `docs/product/storm.json`, `docs/product/reports/coverage.md`, `features/storm/st-0011-server-storage.feature`, previous live integration SPEC, `Program.cs`, `Startup.cs`, `AppHost.cs`, `TaskService.cs`, `AuthService.cs`, service-model DTO routes, `ServerStorage.cs`, `ServiceStackKey.cs`, `appsettings.json`, current live integration test.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: PASS, spec path and planned files explicit; no code/test/artifact changes outside this SPEC.
  - Contract pass: PASS, acceptance criteria/Gherkin separation preserved; no test annotations planned.
  - Adversarial risk pass: PASS, license/quota, route duplication, encoded IDs, DI bridge and full-suite timeout risks covered.
  - Re-review after fixes / Fix and re-review: PASS, no blocking rewrite required after review.
  - Stop decision: PASS, wait for `Спеку подтверждаю`.
- Evidence inspected:
  - Current `CV-0002` status and `AC-0033` coverage note in `storm.json`.
  - `SC-0011-002` feature tags with `TS-0017/18/19`.
  - `TaskService` authenticated methods and DTO route attributes.
  - Production `AppHost` plugin/license behavior and prior `TS-0019` minimal AppHost.
  - `ServerStorage` usage of `JsonServiceClient` for `GetTask`, `GetAllTasks`, `BulkInsertTasks`.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, no ST-0014/ST-0015/UI/BDD runner scope.
  - Acceptance criteria: PASS, targets `AC-0033` and keeps `full` conditional on evidence.
  - Validation evidence: PASS, commands include build, targeted tests, STORM validator and diff check.
  - Unsupported claims: PASS, spec does not claim ServiceStack full coverage before EXEC.
  - Regression / edge case: PASS, route duplicate, encoded slash, user-scope and license quota risks are explicit.
  - Comments/docs/changelog: PASS, product artifacts in Russian; changelog not required.
  - Hidden contract change: PASS, production route/license/schema changes are forbidden.
  - Manual-review challenge: reviewer would likely ask whether minimal AppHost is "production enough"; spec answers by defining it as ServiceStack task API live smoke and preserving exact production AppHost/license as a separate concern.
- No-findings justification: SPEC has one concrete problem, bounded preferred implementation, evidence rules and blocker handling; residual risks are explicitly carried forward.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | fidelity | Minimal test AppHost may not prove production `ServiceStackKey` startup behavior | Keep claim scoped to ServiceStack task API path; do not mark license/startup as covered | accepted-risk |

- Fixed before continuing: не применимо.
- Checks rerun: central stack/template/governance docs and selected repo files inspected.
- Needs human: требуется `Спеку подтверждаю`.
- Residual risks / follow-ups: if exact production AppHost/license startup must be covered, create separate infrastructure/license SPEC.

### Post-EXEC Review
- Статус: EXEC выполнен, stop rule сработал, blocker зафиксирован.
- Scope reviewed: `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`, `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/bdd-lint.md`, `docs/product/reports/traceability.md`, `docs/product/reports/ranking.md`, `docs/product/reports/stories.md`.
- Decision: не добавлять `TS-0020` и не менять production license/routes/DTO. `CV-0002` остаётся `critical`, но переводится в explicit blocker: ServiceStack task API live smoke требует отдельного licensed/test-host strategy.
- Review passes:
  - Scope/Evidence pass: PASS, попытка ограничена ServiceStack task API smoke для `AC-0033`; unrelated UI/ST-0014/ST-0015 scope не затронут.
  - Contract pass: PASS, acceptance criteria не заменены Gherkin, `SC-0011-002` не получил новый test link без passing evidence.
  - Adversarial risk pass: PASS, license/quota blocker подтверждён фактическим targeted run.
  - Re-review after fixes / Fix and re-review: PASS, failing attempted smoke removed from executable test surface; retained `TS-0019` rerun passed.
  - Stop decision: PASS, SPEC требовала остановиться при ServiceStack quota/licensing blocker.
- Evidence inspected:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed` failed during attempted ServiceStack API smoke with `ServiceStack.LicenseException: The free-quota limit on '10 ServiceStack Operations' has been reached`.
  - Same targeted command passed 1/1 after rollback to retained `TS-0019` scope.
  - `rg` confirmed no retained `LiveServiceStackTaskApi`, `TS-0020` test helper or attempted smoke names remain in `ServerStorageLiveIntegrationTests.cs`.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, only artifact blocker sync remains after removing attempted smoke.
  - Acceptance criteria: PASS, `AC-0033` remains `critical`, not `full`.
  - Validation evidence: PASS, retained live test passes; STORM validator and diff checks rerun after artifact sync.
  - Unsupported claims: PASS, no claim of ServiceStack endpoint live coverage was added.
  - Regression / edge case: PASS, route duplication and encoded ID risks remain unresolved because endpoint assertions never ran.
  - Comments/docs/changelog: PASS, product artifacts updated in Russian.
  - Hidden contract change: PASS, production `ServiceStackKey`, routes, DTOs and behavior untouched.
  - Manual-review challenge: reviewer may ask whether a narrower ServiceStack host can register only `TaskService`; current stop decision follows SPEC because assembly-based minimal AppHost already hits ServiceStack operation quota, and route/license strategy now needs a separate design decision.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | ServiceStack live smoke | Minimal test-only AppHost with `TaskService` assembly hits ServiceStack free-quota operation registration before endpoint assertions | Create separate QUEST/SPEC for licensed/test-host strategy or choose to deprioritize `CV-0002` | blocker |

- Fixed before final report: attempted failing smoke/test-only AppHost changes removed; artifact reports updated with blocker.
- Checks rerun: retained `ServerStorageLiveIntegrationTests` passed 1/1; STORM validator and diff checks rerun after artifact sync.
- Validation evidence: see reports and final response.
- Unrelated changes: existing dirty STORM/test changes from prior approved deliveries preserved.
- Needs human: choose whether to open a separate SPEC to remove ServiceStack API smoke blocker or continue `/storm:cover` with another ranked item.
- Residual risks / follow-ups: `AC-0033` still lacks live HTTP ServiceStack task endpoint evidence; `CV-0002` cannot reach full coverage until license/test-host strategy is resolved.

## Approval
Утверждено пользователем фразой: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создать follow-up SPEC для ServiceStack task API live smoke `ST-0011` | 0.84 | Feasibility of minimal ServiceStack AppHost/Funq DI bridge confirms only in EXEC | Запросить `Спеку подтверждаю` | Да | Да, пользователь сказал "делай", что interpreted as request to draft next SPEC, not EXEC approval phrase | QUEST requires SPEC before test/code/artifact changes; next gap is production ServiceStack task API live smoke under `CV-0002` | `specs/2026-06-16-storm-cover-server-storage-servicestack-api-live-smoke.md` |
| EXEC | Attempt ServiceStack task API live smoke | 0.72 | Whether ServiceStack can register only task operations without license quota; endpoint assertions did not run | Stop and artifact-sync blocker | Да | Да, пользователь подтвердил SPEC; stop rule сработал автоматически по evidence | Minimal test-only AppHost reached ServiceStack startup but failed on free-quota operation registration before API assertions; production license flow/routes/DTO were not changed | `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` attempted then rolled back |
| EXEC | Sync STORM blocker and reports | 0.9 | Product decision: resolve ServiceStack license/test-host strategy or move to next ranked gap | Run validators and report next step | Да | Нет, next decision pending | `TS-0020` not retained; `CV-0002` marked blocker while existing `TS-0017/18/19` trace is preserved | `docs/product/storm.json`, `docs/product/reports/*.md`, this SPEC |
