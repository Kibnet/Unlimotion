# STORM /storm:cover: ServiceStack API smoke через narrow registration для ST-0011

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
  - После утверждения разрешены только изменения, прямо связанные с narrow ServiceStack task API registration smoke для `ST-0011`, `AC-0033`, `SC-0011-002`, `CV-0002`.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие артефакты.
  - Не заменять acceptance criteria на Gherkin.
  - Не удалять существующие `TS-0017`, `TS-0018`, `TS-0019`, stories, tests, conflicts, dependencies и reports.
  - Не менять test annotations без отдельного подтверждения.
  - Не менять `ServiceStackKey`, production license flow, `appsettings.json` secrets, public routes или DTO shape.
  - Не добавлять paid/commercial license и не обращаться к внешнему trial/license сервису.
  - UI tests не обязательны, пока UI-facing behavior не меняется; если затрагивается UI, остановиться и расширить SPEC.
  - Если narrow registration невозможен без production route/DTO/license changes, остановиться и оставить `CV-0002` blocker.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/traceability.md`
  - `docs/product/reports/ranking.md`
  - `features/storm/st-0011-server-storage.feature`
  - `specs/2026-06-16-storm-cover-server-storage-servicestack-api-live-smoke.md`
  - `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`
  - `src/Unlimotion.Server.ServiceInterface/TaskService.cs`
  - `src/Unlimotion.Server.ServiceModel/Task.cs`

Если пользователь утверждает эту SPEC фразой `Спеку подтверждаю`, это разрешает перейти в EXEC и внести минимальные test/artifact changes в рамках этой спеки.

## 1. Overview / Цель
Цель: снять текущий blocker `CV-0002` без изменения production license flow через test-only ServiceStack host, который регистрирует только `TaskService` вместо сканирования всей production service assembly.

Outcome contract:
- Success means:
  - test-only AppHost стартует локально без ServiceStack free-quota exception;
  - live HTTP ServiceStack path проверяет authenticated `BulkInsertTasks`, `GetAllTasks` и `GetTask` через `JsonServiceClient`;
  - test подтверждает user-scope: владелец видит свою задачу, другой пользователь не получает чужую задачу через list/load;
  - `TS-0020` добавлен только после passing targeted run;
  - `AC-0033`, `SC-0011-002`, `CV-0002`, feature tags and reports синхронизированы по фактическому evidence.
- Итоговый артефакт / output:
  - passing `TS-0020` или сохранённый blocker с конкретным runtime evidence;
  - минимальные test-only fixture changes;
  - обновлённые `storm.json`, `.feature` tag and reports only if evidence changes;
  - заполненный Post-EXEC review.
- Stop rules:
  - Не больше двух попыток narrow registration strategy.
  - Остановиться, если ServiceStack startup всё ещё падает на quota/license.
  - Остановиться, если test требует production license mutation, external network, route attribute change, DTO shape change, auth semantics change, persisted schema change or paid license.
  - Остановиться, если duplicate route `/tasks` или encoded slash for `TaskItem/<id>` требует public API redesign.

## 2. Текущее состояние (AS-IS)
- `CV-0002` имеет статус `blocked_service_stack_api_license_quota_gap_remaining`.
- `TS-0017/TS-0018` покрывают contract/security и `GetTask` user-scope regression.
- `TS-0019` покрывает live SignalR/RavenDB delivery через real `ChatHub` и isolated RavenDB services.
- Предыдущая попытка ServiceStack task API smoke через minimal AppHost с `typeof(TaskService).Assembly` упала на startup:
  - `ServiceStack.LicenseException: The free-quota limit on '10 ServiceStack Operations' has been reached`.
- Причина вероятна: assembly scan видит больше 10 operations в `Unlimotion.Server.ServiceInterface` / ServiceModel.
- Локальный ServiceStack package `10.0.6` содержит API:
  - `ServiceStackHost.RegisterService(Type, string[])`
  - `ServiceStackServicesOptions.ServiceTypes`
  - `ServiceStackServicesOptions.ServiceRoutes`
- `TaskService` фактически реализует 3 request operations:
  - `Get(GetAllTasks)`
  - `GetAsync(GetTask)`
  - `Post(BulkInsertTasks)`
- DTO routes:
  - `GetAllTasks` -> `GET /tasks`
  - `GetTask` -> `GET /tasks/{Id}`
  - `BulkInsertTasks` -> `POST /tasks/bulk`
  - `GetTasks` также имеет `GET /tasks`, но `TaskService` не содержит handler для `GetTasks`; narrow registration должна не включать этот DTO.

## 3. Проблема
Корневая проблема: текущий ServiceStack API live smoke блокируется не бизнес-поведением `TaskService`, а способом регистрации ServiceStack operations. Assembly scan превышает free quota до выполнения endpoint assertions, поэтому `AC-0033` не может получить live HTTP evidence.

## 4. Цели дизайна
- Разделение ответственности: test-only AppHost отвечает за narrow ServiceStack registration; production `Startup`/`AppHost` не меняются.
- Повторное использование: reuse existing live RavenDB/temp host/JWT patterns from `TS-0019`.
- Тестируемость: deterministic targeted TUnit test without external network/license mutation.
- Консистентность: trace chain remains `Story -> AC -> Scenario -> Test -> Code`.
- Обратная совместимость: no public route, DTO, auth, RavenDB schema, SignalR or UI behavior changes.

## 5. Non-Goals
- Не чинить production ServiceStack license/bootstrap.
- Не добавлять коммерческий ServiceStack license.
- Не менять `ServiceStackKey`.
- Не менять route attributes или DTO shape.
- Не менять `TaskService` behavior, кроме отдельной SPEC если endpoint assertion reveals product bug.
- Не добавлять executable Gherkin runner or step definitions.
- Не менять UI.
- Не закрывать Telegram, platform shells, notification UX or attachment backlog.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` -> добавить test-only narrow ServiceStack AppHost и `TS-0020`.
- Existing live fixture -> temp content root, isolated RavenDB config, Kestrel URL, JWT creation, `JsonServiceClient` helper.
- `LiveServiceStackTaskApiNarrowAppHost` -> стартует ServiceStack без production assembly scan и регистрирует только `TaskService`.
- `docs/product/storm.json` -> add/update `TS-0020`, `AC-0033`, `SC-0011-002`, `CV-0002` after evidence.
- `features/storm/st-0011-server-storage.feature` -> add `@test:TS-0020` only after passing evidence.
- `docs/product/reports/*` -> sync coverage, BDD sync/lint, traceability and ranking status.

### 6.2 Детальный дизайн
- Preferred implementation path:
  1. Добавить второй режим live fixture, например `LiveIntegrationHostMode.ServiceStackTaskApiNarrow`.
  2. В test-only AppHost не передавать production service assembly в constructor; использовать test assembly or empty marker assembly.
  3. Зарегистрировать только `TaskService` через один из локально подтверждённых ServiceStack APIs:
     - `RegisterService(typeof(TaskService), ...)`, если route attributes request DTOs подхватываются корректно;
     - или `ServiceStackServicesOptions.ServiceTypes` / `ServiceRoutes`, если это нужно для регистрации одного service type.
  4. Зарегистрировать Funq dependencies: `IDocumentStore`, `IAsyncDocumentSession`, `IMapper`.
  5. Настроить `JwtAuthProvider` по existing live fixture pattern, без стандартных auth routes.
  6. Использовать `JsonServiceClient(BaseUrl)` with `BearerToken`.
  7. Выполнить `PostAsync(new BulkInsertTasks { ... })`, `GetAsync(new GetAllTasks())`, `GetAsync(new GetTask { Id = storedTaskId })`.
- Data flow:
  - Test creates owner token and other-user token in isolated RavenDB.
  - Owner inserts a task with unprefixed local id through `BulkInsertTasks`.
  - Expected stored id is `TaskItem/<local id>`.
  - Owner reads through `GetAllTasks` and `GetTask`.
  - Other user cannot observe owner task through `GetAllTasks`; `GetTask` must not return owner task data.
- Evidence rules:
  - `TS-0020` can be added only after targeted run passes.
  - If ServiceStack startup fails on quota/license, preserve blocker and do not retain failing test.
  - If route binding requires manual routes that diverge from production DTO route attributes, stop.
  - If `GetTask` ID encoding fails for actual `JsonServiceClient` behavior, stop and record route/encoding blocker.
  - If cross-user `GetTask` returns 404/500/no-result, it can count as non-leak evidence only if owner path passes and no other user's data is returned.
- Visual planning artifact для UI-facing изменений: `Не применимо`, UI не меняется.
- UI test video evidence для UI automation задач: `Не применимо`, UI automation не затрагивается.
- Границы сохранения поведения:
  - permitted: test-only AppHost/fixture/test additions; STORM artifact sync after evidence;
  - forbidden: public routes, DTOs, production license bootstrap, persisted schema, auth model or UI behavior.
- Обработка ошибок:
  - Startup quota/license -> stop and artifact blocker.
  - DI resolution failure -> fix only test-only Funq registration; stop if production architecture changes are needed.
  - Route/encoding limitation -> stop and propose separate implementation SPEC.
- Производительность:
  - Targeted test should stay bounded and isolated.
  - No retries against external services.

## 7. Бизнес-правила / Алгоритмы
- `BulkInsertTasks` stores tasks under current authenticated `UserAuthId`.
- `GetAllTasks` returns only current user's tasks.
- `GetTask` must not return another user's task.
- Coverage level remains `critical` until both ServiceStack API live path and SignalR live delivery are evidenced without unresolved blockers.
- `AC-0033` becomes `full` only if the live API test covers current supported server-storage contract and no route/license blocker remains.

## 8. Точки интеграции и триггеры
- `TaskService.Post(BulkInsertTasks)`
- `TaskService.Get(GetAllTasks)`
- `TaskService.GetAsync(GetTask)`
- `JwtAuthProvider` / `[Authenticate]`
- `AddRavenDbServices()`
- `JsonServiceClient`
- `storm.json`, `.feature` files and reports after validation.

## 9. Изменения модели данных / состояния
- Production persisted model: не меняется.
- RavenDB indexes/schema: не меняются.
- Test state: temporary users/tasks/tokens in isolated RavenDB database.
- STORM state: допускается `TS-0020` and updated links/status/coverage notes only after passing evidence.

## 10. Миграция / Rollout / Rollback
- Миграция production данных: не применимо.
- Rollout: обычный PR/working branch with tests and artifacts.
- Rollback: revert new test/fixture changes and related artifact sync.
- Backward compatibility: public API routes, DTOs and storage schema remain unchanged.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `AC-0033`: live ServiceStack task API smoke proves authenticated `BulkInsert/GetAll/GetTask` over HTTP against real RavenDB services.
  - `SC-0011-002`: linked to `TS-0020` only after passing targeted run.
  - `CV-0002`: status updated strictly by evidence: `covered`, `critical_with_route_gap`, or still `blocked`.
  - Existing `TS-0017/TS-0018/TS-0019` remain linked and green.
- Какие тесты добавить/изменить:
  - `ServerStorage_LiveServiceStackTaskApi_BulkInsertGetAllAndGetTask_RoundTripsAuthenticatedUserTasks`.
- Characterization tests / contract checks:
  - preserve `ServerStorageBddContractTests`.
  - preserve `ServerStorage_LiveSignalR_SaveTask_DeliversUpdateToSecondClientForSameUser`.
- Visual acceptance для UI-facing изменений: `Не применимо`.
- UI video evidence для UI-facing фич/багфиксов: `Не применимо`.
- Базовые замеры до/после для performance tradeoff: `Не применимо`.
- Команды для проверки:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
- Stop rules для test/retrieval/tool/validation loops:
  - If narrow registration fails to compile after two local API attempts, stop and record blocker.
  - If ServiceStack startup quota/license remains, stop and record blocker.
  - If full suite is too expensive or repeats prior timeout, report targeted evidence and residual risk instead of hiding it.

## 12. Риски и edge cases
- Риск: `RegisterService` route parameters require route strings that do not preserve DTO route attributes. Смягчение: stop instead of inventing a different public API contract.
- Риск: `AuthFeature` itself registers enough operations to exceed quota. Смягчение: keep standard auth routes disabled; stop if quota still fails.
- Риск: ServiceStack DI cannot resolve RavenDB scoped session. Смягчение: test-only Funq registration; no production architecture change.
- Риск: `GetTask` route cannot handle `TaskItem/<id>` through `JsonServiceClient`. Смягчение: record route/encoding blocker; no route mutation.
- Риск: duplicate `/tasks` route remains in DTO model. Смягчение: narrow registration should avoid `GetTasks`; if not, record blocker.
- Риск: `BulkInsert` eventual indexing makes immediate reads stale. Смягчение: bounded wait only if RavenDB semantics require it; no unbounded retry.

## 13. План выполнения
1. Зафиксировать EXEC start: `git status --short`, current `CV-0002` blocker state.
2. Add test-only narrow ServiceStack AppHost mode without production service assembly scan.
3. Try `RegisterService(typeof(TaskService), ...)` or equivalent `ServiceTypes` narrow registration.
4. Add `JsonServiceClient` helper and live API test.
5. Run targeted live test.
6. If quota/license persists, remove failing attempted test and sync blocker only.
7. If route/encoding/product behavior fails, stop and sync precise blocker.
8. If passing, add `TS-0020`, update `SC-0011-002` tags and STORM reports.
9. Run build, scoped tests, STORM validator and diff checks.
10. Fill Post-EXEC review.

## 14. Открытые вопросы
- Блокирующих вопросов нет до EXEC: первый шаг сам проверяет feasibility narrow registration.
- Неблокирующий риск: exact ServiceStack registration API may require choosing between `RegisterService`, `ServiceTypes` or `ServiceRoutes` during EXEC.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `dotnet-backend-api`, `dotnet-ravendb`, `testing-dotnet`.
- Выполненные требования профиля:
  - `/storm:cover` with tests/artifact changes goes through QUEST.
  - Existing STORM artifacts are preserved; `/storm:full-cycle` is not restarted.
  - Acceptance criteria are not replaced by Gherkin.
  - Scenario/Test/coverage status changes only after actual evidence.
  - TUnit validation uses `--treenode-filter`, not VSTest `--filter`.
  - UI override is accounted for: no UI-facing change.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` | Add narrow ServiceStack task API live smoke and test-only AppHost if approved | Evidence for `AC-0033` ServiceStack API gap |
| `docs/product/storm.json` | Add `TS-0020` and update `CV-0002` only after passing evidence, or preserve blocker | STORM trace sync |
| `features/storm/st-0011-server-storage.feature` | Add `@test:TS-0020` to `SC-0011-002` only after passing run | Gherkin layer sync |
| `docs/product/reports/bdd-sync.md` | Update Scenario -> Test sync | Reflect actual evidence/blocker |
| `docs/product/reports/bdd-lint.md` | Update warnings/gaps | Reflect actual evidence/blocker |
| `docs/product/reports/coverage.md` | Update `CV-0002` and behavior metrics | Continue `/storm:cover` |
| `docs/product/reports/traceability.md` | Add ServiceStack task API trace if passing | Audit chain |
| `docs/product/reports/ranking.md` | Update `CV-0002` status | Keep backlog honest |
| this SPEC | EXEC journal and Post-EXEC review | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `AC-0033` | `critical`, tests `TS-0017`, `TS-0018`, `TS-0019`; ServiceStack API live smoke blocked | `full` only if narrow live API smoke passes; otherwise `critical` with precise blocker |
| `SC-0011-002` | passing contract/security/live SignalR evidence | passing + `TS-0020` only after passing ServiceStack API evidence |
| `CV-0002` | `blocked_service_stack_api_license_quota_gap_remaining` | `covered_by_live_task_api_and_signalr_tests` or narrower blocker |
| `ST-0011` | partial story with strong auth/security/live SignalR evidence | partial or stronger only if live API path succeeds |

## 18. Альтернативы и компромиссы
- Вариант: Add paid/commercial ServiceStack license for tests.
  - Плюсы: maximum production fidelity.
  - Минусы: depends on external/commercial state and touches license management.
  - Почему не выбран: user requested repo-local continuation; current constraints forbid license mutation.
- Вариант: Keep ServiceStack API smoke blocked and move to `ST-0014`.
  - Плюсы: avoids infrastructure blocker.
  - Минусы: leaves `AC-0033` below full coverage.
  - Почему не выбран сейчас: user asked to continue immediately after `CV-0002` blocker, and local ServiceStack API exposes narrow registration APIs worth a bounded probe.
- Вариант: Change production routes to remove duplicate `/tasks`.
  - Плюсы: may clarify API contract.
  - Минусы: public API behavior change.
  - Почему не выбран: outside this SPEC.
- Вариант: Directly call `TaskService` without ServiceStack HTTP pipeline.
  - Плюсы: avoids license quota.
  - Минусы: duplicates existing contract/source tests and does not close live HTTP API gap.
  - Почему не выбран: `CV-0002` specifically needs ServiceStack pipeline evidence.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals and non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | Narrow registration path, data flow, integration points and rollback defined. |
| C. Безопасность изменений | 11-13 | PASS | Production license/routes/DTO/schema changes forbidden; stop rules explicit. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, target test and validation commands listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan and alternatives allow bounded EXEC until passing evidence or blocker. |
| F. Соответствие профилю | 20 | PASS | STORM, QUEST, .NET backend/RavenDB and TUnit rules covered. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope limited to `AC-0033` ServiceStack task API smoke blocker. |
| 2. Понимание текущего состояния | 5 | Captures `TS-0017/18/19`, current blocker, previous quota failure and narrow registration API evidence. |
| 3. Конкретность целевого дизайна | 5 | Preferred AppHost mode, registration options, test flow and stop conditions are defined. |
| 4. Безопасность (миграция, откат) | 5 | No data migration; public API/license changes explicitly excluded. |
| 5. Тестируемость | 5 | Targeted commands and evidence rules are explicit. |
| 6. Готовность к автономной реализации | 5 | EXEC can proceed with bounded attempts and clear rollback/blocker handling. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: this SPEC, central stack, local override, `docs/product/reports/ranking.md`, previous ServiceStack API smoke blocker SPEC, `TaskService.cs`, `Task.cs`, ServiceStack package XML docs for `RegisterService` and `ServiceTypes`.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: PASS, planned mutations are bounded to test-only harness and STORM sync.
  - Contract pass: PASS, no acceptance criteria/Gherkin replacement and no `TS-0020` without passing evidence.
  - Adversarial risk pass: PASS, quota/license, route duplication, encoded IDs, DI and full-suite timeout risks covered.
  - Re-review after fixes / Fix and re-review: PASS, no blocking rewrite required.
  - Stop decision: PASS, wait for `Спеку подтверждаю`.
- Evidence inspected:
  - Current ranking says `CV-0002` remains blocked by ServiceStack free-quota operation registration.
  - ServiceStack package XML exposes manual service registration APIs.
  - `TaskService` has three task operations, below the free quota if registration can stay narrow.
  - `Task.cs` includes duplicate `/tasks` DTO route risk through `GetTasks` vs `GetAllTasks`.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, no ST-0014/CV-0006/UI scope.
  - Acceptance criteria: PASS, `AC-0033` full remains conditional on live evidence.
  - Validation evidence: PASS, commands include build, scoped tests, STORM validator and diff check.
  - Unsupported claims: PASS, spec does not claim narrow registration will work before EXEC.
  - Regression / edge case: PASS, quota, route and encoding blockers explicitly stop the task.
  - Comments/docs/changelog: PASS, product artifacts in Russian; changelog not required.
  - Hidden contract change: PASS, production route/license/schema changes forbidden.
  - Manual-review challenge: reviewer may ask whether `RegisterService` route strings preserve production DTO routes; spec handles this by stopping if route registration diverges from DTO attributes.
- No-findings justification: SPEC has a single problem, bounded implementation, evidence rules and blocker handling.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | feasibility | Exact ServiceStack narrow registration API choice is confirmed only in EXEC | Limit attempts to `RegisterService`/`ServiceTypes` and stop on compile/startup blocker | accepted-risk |

- Fixed before continuing: не применимо.
- Checks rerun: central stack docs, ranking, ServiceStack XML docs and selected repo files inspected.
- Needs human: требуется `Спеку подтверждаю`.
- Residual risks / follow-ups: if narrow registration cannot preserve production DTO route behavior, leave `CV-0002` blocked and continue with another ranked item.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`, `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, `docs/product/reports/*`, targeted test output, STORM validator output, `git diff --check`.
- Decision: можно завершать; `CV-0002` закрыт для текущего supported server-storage contract.
- Review passes:
  - Scope/Evidence pass: PASS, изменения ограничены approved test-only ServiceStack harness and STORM sync.
  - Contract pass: PASS, production `ServiceStackKey`, routes, DTO shape, auth semantics and persisted schema не менялись; acceptance criteria не заменены Gherkin.
  - Adversarial risk pass: PASS, ServiceStack quota blocker снят через narrow `TaskService` registration; duplicate `/tasks` DTO route не расширялся в test host; `JsonServiceClient` successfully exercised actual DTO route attributes.
  - Re-review after fixes / Fix and re-review: PASS, no unresolved blocker after validation.
  - Stop decision: PASS, stop rules не сработали после successful narrow registration.
- Evidence inspected:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore` passed with existing warnings.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed` passed 2/2.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed` passed 7/7.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` returned OK, 0 errors, 0 warnings.
  - `git diff --check` passed, only line-ending warnings from Git.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, no ST-0014/CV-0006/UI scope.
  - Acceptance criteria: PASS, `AC-0033` raised to `full` only after live ServiceStack and SignalR evidence.
  - Validation evidence: PASS, scoped tests, build, STORM validator and diff checks completed.
  - Unsupported claims: PASS, production AppHost/license startup is not claimed as covered; current claim is supported ServiceStack HTTP task API path.
  - Regression / edge case: PASS, cross-user non-leak assertion included; `GetTask` route handled `TaskItem/<id>` through `JsonServiceClient`.
  - Comments/docs/changelog: PASS, product artifacts in Russian; changelog not required.
  - Hidden contract change: PASS, production code unchanged in this EXEC.
  - Manual-review challenge: reviewer may ask whether test-only narrow registration is production-equivalent. Answer: it intentionally proves ServiceStack HTTP routing/auth/DTO binding/TaskService/RavenDB for the supported task API path without claiming production license bootstrap coverage.
- No-findings justification: target behavior has passing integration evidence and artifact trace is synchronized.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | fidelity | Exact production AppHost/license bootstrap remains outside `AC-0033` coverage claim | Keep as operational deployment concern unless product owner requests release/deployment smoke | accepted-risk |

- Fixed before final report: не применимо.
- Checks rerun: build, scoped live/BDD tests, STORM validator, whitespace and diff checks.
- Validation evidence: see above.
- Unrelated changes: none beyond approved SPEC scope in this EXEC; existing repository state preserved.
- Needs human: выбрать следующий `/storm:cover` item: `ST-0014` Telegram bot or `CV-0006` notification/error UX.
- Residual risks / follow-ups: full-suite run remains previously timeout-prone; scoped server-storage evidence is green.

## Approval
Утверждено пользователем фразой: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создать follow-up SPEC для narrow ServiceStack task API registration | 0.82 | Exact ServiceStack registration API behavior confirms only in EXEC | Запросить `Спеку подтверждаю` | Да | Да, пользователь сказал "продолжай", что interpreted as request to prepare the next SPEC, not EXEC approval phrase | Current `CV-0002` blocker has a bounded local probe via ServiceStack `RegisterService`/`ServiceTypes`; QUEST requires SPEC before test/code changes | `specs/2026-06-16-storm-cover-server-storage-servicestack-narrow-registration.md` |
| EXEC | Реализовать narrow ServiceStack task API smoke | 0.9 | Full-suite stability outside scoped server-storage validation | Sync artifacts and validate | Нет | Да, пользователь подтвердил SPEC фразой "Спеку подтверждаю" | `RegisterService(typeof(TaskService))` avoided assembly scan quota and allowed live `BulkInsert/GetAll/GetTask` assertions through `JsonServiceClient` | `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` |
| EXEC | Синхронизировать STORM artifacts and reports | 0.92 | Следующий product coverage priority | Завершить и предложить next step | Да | Нет, next choice pending | Passing `TS-0020` justifies `CV-0002=covered_by_live_task_api_and_signalr_tests` and `AC-0033=full` | `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, `docs/product/reports/*`, this SPEC |
