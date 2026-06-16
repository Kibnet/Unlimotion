# STORM /storm:cover: security и integration follow-up для ST-0011

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
  - локальный `AGENTS.override.md`
- Ограничения:
  - До явного утверждения SPEC не менять production code, tests, test annotations, STORM artifacts и поведение продукта.
  - После утверждения разрешены только изменения, прямо связанные с `ST-0011`, `AC-0033`, `SC-0011-002`, `CV-0002` и security/integration gap server storage.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие артефакты.
  - Не заменять acceptance criteria на Gherkin.
  - UI tests не обязательны, пока не меняется UI-facing flow.
  - Если live RavenDB/SignalR harness требует нестабильного внешнего сервиса, длительного server process или новых инфраструктурных зависимостей без явной пользы, остановиться и оставить отдельный infrastructure SPEC.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/bdd-lint.md`
  - `features/storm/st-0011-server-storage.feature`
  - `specs/2026-06-14-storm-cover-server-storage-bdd.md`

Если пользователь утверждает эту SPEC фразой `Спеку подтверждаю`, это разрешает перейти в EXEC и внести минимальные test/code/artifact changes в рамках этой спеки.

## 1. Overview / Цель
Цель: продолжить `/storm:cover` по `ST-0011` и закрыть главный оставшийся security gap в `AC-0033`: `TaskService.GetTask` должен не раскрывать задачу другого authenticated user. Дополнительно нужно проверить, можно ли без хрупкой инфраструктуры усилить live RavenDB/SignalR evidence; если нельзя, gap должен остаться явным, а не маскироваться contract-level тестом.

Outcome contract:
- Success means:
  - Есть reproducing test для user-scope правила `GetTask`.
  - Если test выявляет текущий cross-user leak, production fix минимально добавляет фильтр authenticated user id в `TaskService.GetTask`.
  - `SC-0011-002` получает дополнительную связь с новым test evidence.
  - `CV-0002` снижен: security/user-scope часть закрыта, live SignalR/RavenDB integration либо покрыт stable smoke test, либо оставлен explicit follow-up.
  - STORM reports синхронизированы только по фактическому evidence.
- Итоговый артефакт / output:
  - regression/contract tests в `src/Unlimotion.Test`.
  - при необходимости минимальный fix в `src/Unlimotion.Server.ServiceInterface/TaskService.cs`.
  - обновлённые `storm.json`, `bdd-sync.md`, `bdd-lint.md`, `coverage.md`, `traceability.md`.
  - validation evidence с targeted/full test commands.
- Stop rules:
  - Остановиться, если тестовая обвязка требует нестабильного live server/RavenDB/SignalR процесса.
  - Остановиться, если исправление требует изменения публичного API, DTO route, auth модели или миграции persisted data.
  - Остановиться, если обнаружится более широкий security issue за пределами `GetTask` user scope.

## 2. Текущее состояние (AS-IS)
- `ST-0011` имеет status `partial`, но поддерживаемость optional server storage подтверждена как `confirmed_supported_optional_surface`.
- `AC-0032` покрыт `TS-0017` на уровне contract coverage.
- `AC-0033` остаётся `partial`: есть `TS-0017`, но `coverage_note` явно фиксирует live RavenDB/SignalR integration и `GetTask` user-scope как follow-up.
- `SC-0011-002` имеет status `passing`, но `automation_status = passing_contract_coverage`, не live integration coverage.
- `TaskService.GetAllTasks` вызывает `Request.ThrowIfUnauthorized()`, берёт `session.UserAuthId` и фильтрует задачи по `UserId`.
- `TaskService.BulkInsertTasks` вызывает `Request.ThrowIfUnauthorized()`, берёт `session.UserAuthId` и сохраняет `task.UserId = uid`.
- `TaskService.GetTask` вызывает `Request.ThrowIfUnauthorized()`, но сейчас ищет задачу только по `decodedId`; в текущем коде нет фильтра по `UserId`.
- `ChatHub.SaveTask` и `DeleteTasks` проверяют `uid` при сохранении/удалении через hub, но это не закрывает прямой ServiceStack endpoint `GET /tasks/{Id}`.
- В `src/Unlimotion.Test` уже есть `ServerStorageBddContractTests`, но нет runtime integration harness для RavenDB/ServiceStack/SignalR.
- В проекте найден `Raven.Embedded` в сервере, но не найден готовый `RavenTestDriver`, `WebApplicationFactory` или устойчивый test-host для server storage.

## 3. Проблема
Корневая проблема: `SC-0011-002` заявляет аутентифицированные CRUD endpoints, но для `GetTask` нет доказательства user isolation. По текущему source evidence `GetTask` может раскрыть задачу по id без проверки владельца.

## 4. Цели дизайна
- Разделение ответственности: security/user-scope fix отделён от live integration harness.
- Повторное использование: использовать существующий `src/Unlimotion.Test` и TUnit/Microsoft.Testing.Platform workflow.
- Тестируемость: сначала deterministic failing regression test, затем минимальный fix, затем только stable integration smoke при наличии надёжного seam.
- Консистентность: сохранить trace chain `Story -> AC -> Scenario -> Test -> Code`.
- Обратная совместимость: не менять route, DTO, payload shape и внешний UX.

## 5. Non-Goals (чего НЕ делаем)
- Не реализуем полноценный executable Gherkin runner и `step_definitions`.
- Не меняем auth provider, JWT format, token lifetime или registration/login behavior.
- Не меняем клиентский Settings/UI flow.
- Не меняем `ServerStorage` client behavior, если security fix ограничен server endpoint.
- Не добавляем внешнюю инфраструктуру, Docker requirement или long-running local service без отдельного решения.
- Не закрываем Telegram `ST-0014` и другие ranked gaps.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/ServerStorageBddContractTests.cs` или новый `ServerStorageSecurityTests.cs` -> reproducing test для `TaskService.GetTask` user-scope.
- `src/Unlimotion.Server.ServiceInterface/TaskService.cs` -> минимальный fix: `GetTask` должен учитывать `session.UserAuthId` при поиске задачи.
- `docs/product/storm.json` -> добавить новый test link (`TS-0018`, если будет создан новый тестовый блок) к `AC-0033`, `SC-0011-002`, `CV-0002`.
- `features/storm/st-0011-server-storage.feature` -> добавить `@test:TS-0018` к `SC-0011-002` только после успешного запуска.
- `docs/product/reports/*` -> обновить только coverage/sync/lint/traceability evidence.

### 6.2 Детальный дизайн
- Потоки данных:
  - authenticated request -> `Request.ThrowIfUnauthorized()` -> `session.UserAuthId`.
  - `GetTask` получает URL-decoded task id.
  - query выбирает задачу по `Id` и текущему `UserId`.
  - если задача принадлежит другому user или отсутствует, endpoint не должен вернуть чужой `TaskItemMold`.
- Контракты / API:
  - route `/tasks/{Id}` не меняется.
  - DTO `GetTask` и response `TaskItemMold` не меняются.
  - error mapping сохраняется через текущий ServiceStack exception handling.
- Output contract / evidence rules:
  - Test должен падать на текущем source/behavior до fix или явно доказать, что current implementation уже safe.
  - `passing`/coverage upgrade в STORM ставится только после фактического successful test run.
  - Если live integration smoke не добавлен, coverage report обязан оставить этот gap.
- Visual planning artifact для UI-facing изменений: `Не применимо`, UI не меняется.
- UI test video evidence для UI automation задач: `Не применимо`, UI automation не затрагивается.
- Границы сохранения поведения:
  - Допустимое изменение: task lookup становится строже по authenticated user.
  - Недопустимое изменение: новый route, новый auth scheme, изменение client token flow, изменение SignalR group semantics.
- Обработка ошибок:
  - Для чужой задачи acceptable outcomes: not found / unauthorized-like service error по текущему ServiceStack mapping.
  - Не возвращать payload чужой задачи.
- Производительность:
  - Добавление `UserId` predicate в query не должно добавлять extra round-trip.
  - Не вводить full scan или post-filter после загрузки документа.

## 7. Бизнес-правила / Алгоритмы
- Пользователь может получить через `GetTask` только задачу, где `TaskItem.UserId == session.UserAuthId`.
- `GetAllTasks`, `GetTask`, `BulkInsertTasks` должны иметь единый user-scope invariant.
- SignalR update delivery между клиентами остаётся отдельным integration behavior: если live evidence не стабилен, он не должен блокировать security fix.

## 8. Точки интеграции и триггеры
- `ServerStorage.Load(itemId)` вызывает ServiceStack `GetTask`.
- `TaskService.GetAsync(GetTask request)` выполняет server-side lookup.
- `ChatHub.SaveTask/DeleteTasks` остаются reference behavior для hub-side user isolation, но не заменяют endpoint check.
- STORM sync запускается после успешных tests и только обновляет traceability.

## 9. Изменения модели данных / состояния
- Persisted model: не меняется.
- RavenDB indexes: не планируется менять.
- Data migration: не требуется.
- STORM state: добавляется/обновляется только test evidence и coverage status для `AC-0033`/`SC-0011-002`/`CV-0002`.

## 10. Миграция / Rollout / Rollback
- Rollout: обычный PR с regression test, минимальным server fix и artifact sync.
- Rollback: revert test/code/artifact changes.
- Backward compatibility: корректные пользователи продолжат получать свои задачи; попытка получить чужую задачу перестанет возвращать payload.
- Production data migration: не применимо.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `AC-0033`: `GetTask` не возвращает задачу другого authenticated user.
  - `SC-0011-002`: получает additional passing evidence для user-scope security.
  - `CV-0002`: security/user-scope часть закрыта или явно зафиксирована как blocked с причиной.
- Какие тесты добавить/изменить:
  - `TaskService_GetTask_PreservesAuthenticatedUserScope`
  - при stable seam: `TaskService_GetTask_DoesNotReturnOtherUsersTask`
  - при stable seam: `ServerStorage_SignalR_Update_DeliversTaskChangeToOtherClient`
- Characterization tests / contract checks:
  - Первый тест должен подтвердить, что `GetTask` использует `session.UserAuthId` в query.
  - Если runtime ServiceStack/RavenDB fixture удаётся стабилизировать без внешней инфраструктуры, добавить behavioral test с двумя users и задачей чужого user.
- Visual acceptance для UI-facing изменений: `Не применимо`.
- UI video evidence для UI-facing фич/багфиксов: `Не применимо`.
- Базовые замеры до/после для performance tradeoff: `Не применимо`; изменение query predicate не требует perf baseline.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed`
  - если будет новый class: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageSecurityTests/*" --output Detailed`
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build -- --maximum-parallel-tests 1 --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
- Stop rules для test/retrieval/tool/validation loops:
  - Не больше двух попыток стабилизировать live RavenDB/SignalR harness в этой EXEC.
  - Если targeted regression test можно сделать deterministic source/contract test, не ждать live harness.
  - Если full solution build снова блокируется Android/wasm workloads, зафиксировать environment blocker и использовать test project build + full test project run как next-best evidence.

## 12. Риски и edge cases
- Риск: source-level test может быть слабее runtime security test. Смягчение: предпочесть behavioral test, но не блокировать минимальный security fix нестабильной инфраструктурой.
- Риск: `FirstAsync` при чужой задаче вернёт generic error вместо 404. Смягчение: acceptance требует не возвращать payload; точный error mapping не менять без отдельного решения.
- Риск: query по `Id + UserId` может требовать Raven index. Смягчение: Raven LINQ должен построить query; если нужен индекс, остановиться и оформить data/index plan.
- Риск: SignalR smoke может быть flaky. Смягчение: не добавлять flaky tests; оставить explicit integration gap.
- Риск: UI override. Смягчение: UI не меняется; если изменение дойдёт до UI Settings, остановиться и расширить SPEC с UI tests.

## 13. План выполнения
1. Зафиксировать старт EXEC: `git status --short`, текущий approved SPEC и unchanged unrelated artifacts.
2. Добавить reproducing test для `TaskService.GetTask` user-scope.
3. Запустить targeted test и убедиться, что он падает на текущем gap или уже проходит с объективным evidence.
4. Если test падает, минимально исправить `TaskService.GetTask`, добавив `UserId` predicate в query.
5. Перезапустить targeted tests.
6. Оценить live RavenDB/SignalR harness; добавить smoke только если он стабилен за две попытки и не требует новых инфраструктурных предпосылок.
7. Обновить STORM artifacts и reports по фактическому результату.
8. Запустить test project build, full test project run, STORM validator и diff checks.
9. Выполнить Post-EXEC review.

## 14. Открытые вопросы
- Блокирующих вопросов нет.
- Неблокирующий вопрос для EXEC: достаточно ли source/contract security test для текущего `/storm:cover` increment, если live RavenDB fixture окажется нестабильным? Ответ спеки: да, но coverage report обязан оставить live gap.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `dotnet-backend-api`, `dotnet-ravendb`.
- Выполненные требования профиля:
  - `/storm:cover` с code/test changes идёт через QUEST.
  - Acceptance criteria не заменяются Gherkin.
  - Scenario status/coverage меняется только по test evidence.
  - Для RavenDB query/security изменений предусмотрены tests и rollback.
  - Для TUnit используется `--treenode-filter`, а не VSTest `--filter`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/ServerStorageBddContractTests.cs` | Добавлен regression/contract test для `GetTask` user-scope | Закрыть security gap `CV-0002` |
| `src/Unlimotion.Server.ServiceInterface/TaskService.cs` | Минимальный fix query predicate `Id == decodedId && UserId == uid` | Не возвращать чужую задачу по id |
| `docs/product/storm.json` | Добавить test evidence/link/status по факту | Синхронизировать STORM trace |
| `features/storm/st-0011-server-storage.feature` | Добавить test tag к `SC-0011-002` по факту | Синхронизировать BDD layer |
| `docs/product/reports/bdd-sync.md` | Обновить Scenario -> Test sync | Отразить новый evidence |
| `docs/product/reports/bdd-lint.md` | Обновить lint notes/gaps | Не скрывать live integration gap |
| `docs/product/reports/coverage.md` | Обновить `CV-0002` | Продолжение `/storm:cover` |
| `docs/product/reports/traceability.md` | Обновить Story/AC/Scenario/Test/Code trace | Аудит требований |
| `specs/2026-06-16-storm-cover-server-storage-security-integration.md` | Post-EXEC review и журнал | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `AC-0033` | `partial`, есть contract-level `TS-0017` | user-scope security covered test evidence; live integration gap отдельно |
| `SC-0011-002` | `passing_contract_coverage` | additional passing security regression evidence |
| `CV-0002` | partially covered by contract tests | security/user-scope закрыт или blocked с evidence; SignalR/RavenDB live gap явно классифицирован |
| `TaskService.GetTask` | query by decoded id only | query by decoded id and authenticated user id |

## 18. Альтернативы и компромиссы
- Вариант: сразу строить полноценный live integration test server + RavenDB + SignalR clients.
  - Плюсы: максимальная близость к production.
  - Минусы: выше риск flaky suite, дольше feedback loop, нет готового harness.
  - Почему не выбран как обязательный: security gap можно закрыть меньшим deterministic increment.
- Вариант: оставить только artifact gap без code/test fix.
  - Плюсы: быстро.
  - Минусы: известный user-scope риск остаётся незащищённым.
  - Почему не выбран: `/storm:cover` уже перешёл к delivery-task, а риск локализован.
- Вариант: source/contract test без production fix.
  - Плюсы: минимальный scope.
  - Минусы: не закрывает реальный risk, если test только описывает желаемый контракт.
  - Почему не выбран как финал: source/contract test должен вести к минимальному fix, если текущий код не соответствует контракту.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals и non-goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Разведены security fix, live integration и artifact sync; rollback описан. |
| C. Безопасность изменений | 11-13 | PASS | Есть TDD/debug order, stop rules, no UI/data migration scope. |
| D. Проверяемость | 14-16 | PASS | Acceptance, tests, commands и planned files указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План пошаговый; live harness имеет stop rule. |
| F. Соответствие профилю | 20 | PASS | STORM + QUEST + TUnit/RavenDB требования отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope ограничен `ST-0011/AC-0033/SC-0011-002/CV-0002`. |
| 2. Понимание текущего состояния | 5 | Зафиксированы `GetAll`, `BulkInsert`, `GetTask`, ChatHub и отсутствие готового harness. |
| 3. Конкретность целевого дизайна | 5 | Указаны тесты, возможный минимальный fix и artifact sync. |
| 4. Безопасность (миграция, откат) | 5 | Нет миграции; rollback прост; API/DTO не меняются. |
| 5. Тестируемость | 4 | Deterministic regression test задан; live integration зависит от seam. |
| 6. Готовность к автономной реализации | 5 | EXEC может идти автономно с понятными stop rules. |

Итоговый балл: 29 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-storm-cover-server-storage-security-integration.md`, central stack, local override, `docs/product/storm.json`, `docs/product/reports/coverage.md`, `features/storm/st-0011-server-storage.feature`, `TaskService.cs`, `ChatHub.cs`, `StartupExtensions.cs`, `ServerStorageBddContractTests.cs`.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: PASS, planned changes ограничены security/integration gap `ST-0011`.
  - Contract pass: PASS, acceptance criteria и Gherkin не заменяются.
  - Adversarial risk pass: PASS, live integration uncertainty не скрыта и имеет stop rule.
  - Re-review after fixes / Fix and re-review: не требовалось.
  - Stop decision: ждать `Спеку подтверждаю`.
- Evidence inspected:
  - `TaskService.GetAllTasks` фильтрует `UserId`.
  - `TaskService.BulkInsertTasks` проставляет `UserId`.
  - `TaskService.GetTask` сейчас фильтрует только `Id`.
  - `ChatHub.SaveTask/DeleteTasks` имеют hub-side user checks.
  - `src/Unlimotion.Test` не содержит готового Raven test harness.
- Depth checklist:
  - Scope drift / unrelated changes: новые изменения вне текущей SPEC не планируются до approval.
  - Acceptance criteria: покрывается `AC-0033`.
  - Validation evidence: команды TUnit/build/STORM validator заданы.
  - Unsupported claims: live coverage не обещается без stable harness.
  - Regression / edge case: чужой task id, missing task id, Raven query predicate и SignalR flakiness отмечены.
  - Comments/docs/changelog: changelog не нужен до EXEC; comments не планируются.
  - Hidden contract change: API/DTO/route/auth model менять запрещено.
  - Manual-review challenge: ручное ревью вероятно спросит, не слишком ли слаб source-level test; SPEC предпочитает behavioral test, но оставляет deterministic fallback.
- No-findings justification: SPEC ограничивает риск, не маскирует live gap и задаёт проверяемый минимальный security outcome.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Live RavenDB/SignalR harness может оказаться нестабильным | Не делать flaky tests; оставить explicit follow-up | accepted-risk |

- Fixed before continuing: не применимо.
- Checks rerun: чтение instruction stack и code/artifact seams.
- Needs human: требуется `Спеку подтверждаю`.
- Residual risks / follow-ups: полноценный live SignalR delivery test может остаться отдельной infrastructure SPEC.

### Post-EXEC Review
- Статус: PASS с residual unrelated full-suite flake
- Scope reviewed: approved SPEC, `git status --short`, diff по `TaskService.cs`, `ServerStorageBddContractTests.cs`, `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, BDD/coverage/trace reports, TUnit/build evidence.
- Decision: текущий security/user-scope increment завершён; live RavenDB/SignalR integration оставить отдельным follow-up; full-suite headless teardown failures не исправлять в этой SPEC, потому что они unrelated и проходят изолированно.
- Review passes:
  - Scope/Evidence pass: PASS, изменения ограничены `ST-0011/AC-0033/SC-0011-002/CV-0002`.
  - Contract pass: PASS, API route/DTO/auth model не менялись; acceptance criteria не заменялись Gherkin.
  - Adversarial risk pass: PASS, live integration gap не скрыт; full-suite failures классифицированы по isolated evidence.
  - Re-review after fixes / Fix and re-review: PASS, после server fix targeted tests и STORM validator перезапущены.
  - Stop decision: завершать без live harness, потому что stable harness в проекте не найден и SPEC запрещает flaky live server.
- Evidence inspected:
  - `TaskService_GetTask_PreservesAuthenticatedUserScope` до fix: FAIL, `GetTask` не содержал `UserAuthId` predicate.
  - `TaskService_GetTask_PreservesAuthenticatedUserScope` после fix: PASS, 1/1.
  - `ServerStorageBddContractTests`: PASS, 7/7.
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`: PASS, 0 errors, existing warnings.
  - Full `Unlimotion.Test` non-sandbox: FAIL on unrelated ACL-sensitive `BackupViaGitServiceTests`; same test PASS outside sandbox.
  - Full `Unlimotion.Test` outside sandbox: FAIL on unrelated Avalonia.Headless teardown tests; failed tests PASS isolated.
  - Repeat full `Unlimotion.Test` outside sandbox: FAIL on a different unrelated Avalonia.Headless teardown test; failed test PASS isolated.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`: PASS, 0 errors, 0 warnings.
- Depth checklist:
  - Scope drift / unrelated changes: production/test changes касаются только server storage; prior untracked STORM artifacts сохранены.
  - Acceptance criteria: `AC-0033` получил security/user-scope regression evidence; остаётся partial из-за live RavenDB/SignalR gap.
  - Validation evidence: targeted/build/STORM validator зелёные; full-suite residual unrelated flake явно зафиксирован.
  - Unsupported claims: live integration coverage не заявляется.
  - Regression / edge case: чужой task id теперь должен фильтроваться по authenticated user id.
  - Comments/docs/changelog: changelog не требуется; новые code comments не добавлялись.
  - Hidden contract change: route `/tasks/{Id}`, DTO и auth model не менялись; endpoint стал строже по user isolation.
  - Manual-review challenge: source-level regression test слабее live RavenDB behavior test, но соответствует approved deterministic fallback и закрывает локализованный code risk.
- No-findings justification: изменения минимальны, тест сначала воспроизвёл gap, затем прошёл после fix; остаточные failures не связаны с изменённой поверхностью и проходят isolated.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full `Unlimotion.Test` нестабилен из-за unrelated Avalonia.Headless teardown failures | Не чинить в этой SPEC; оставить отдельный test-suite stabilization follow-up при необходимости | accepted-risk |
| LOW | coverage | Live RavenDB/SignalR delivery по `SC-0011-002` всё ещё не покрыт | Оформить отдельную SPEC для stable integration harness | follow-up |

- Fixed before final report: добавлен `UserId` predicate в `TaskService.GetTask`; STORM links/reports обновлены на `TS-0018`.
- Checks rerun: targeted pre/post, `ServerStorageBddContractTests` 7/7, test project build, full-suite triage, STORM validator.
- Validation evidence: targeted/build/artifact validation прошли; full suite имеет unrelated isolated-passing flake.
- Unrelated changes: существующие untracked STORM artifacts и feature files из предыдущего BDD sync не удалялись.
- Needs human: не требуется для завершения текущей SPEC; требуется отдельное решение для live integration harness или full-suite stabilization.
- Residual risks / follow-ups: live RavenDB/SignalR smoke; unrelated Avalonia.Headless full-suite teardown stability.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создать follow-up SPEC для `/storm:cover` по security/integration gap `ST-0011` | 0.87 | Нет approval на EXEC; live RavenDB/SignalR seam подтвердится только в реализации | Запросить `Спеку подтверждаю` | Да | Нет | Пользователь сказал "Двигаемся дальше" после рекомендации отдельной SPEC, поэтому создан SPEC gate без изменения кода | `specs/2026-06-16-storm-cover-server-storage-security-integration.md` |
| EXEC | Добавить reproducing test для `TaskService.GetTask` user-scope | 0.9 | Нет live RavenDB harness; test остаётся deterministic source/regression check | Внести минимальный server fix после failing evidence | Нет | Да, пользователь подтвердил SPEC фразой `Спеку подтверждаю` | Тест упал до fix на отсутствии `UserAuthId` predicate, что подтвердило локализованный security gap | `src/Unlimotion.Test/ServerStorageBddContractTests.cs` |
| EXEC | Исправить `TaskService.GetTask` user isolation | 0.9 | Live behavior test не добавлен из-за отсутствия stable harness | Перезапустить targeted/build checks и синхронизировать STORM artifacts | Нет | Да, approval уже получен | Минимальный predicate `Id == decodedId && UserId == uid` сохраняет route/DTO и закрывает cross-user lookup risk | `src/Unlimotion.Server.ServiceInterface/TaskService.cs` |
| EXEC | Синхронизировать STORM artifacts и validation evidence | 0.86 | Full-suite remains flaky outside changed scope | Завершить Post-EXEC review | Нет | Да, approval уже получен | `TS-0018` связан с `SC-0011-002`; `AC-0033` остаётся partial только из-за live RavenDB/SignalR gap | `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, `docs/product/reports/*.md`, `specs/2026-06-16-storm-cover-server-storage-security-integration.md` |
