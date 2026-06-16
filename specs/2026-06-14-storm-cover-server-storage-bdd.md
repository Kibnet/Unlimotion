# STORM /storm:cover: server storage BDD coverage для ST-0011

## 0. Метаданные
- Тип (профиль): `storm-product-development` + delivery-task/QUEST после утверждения SPEC
- Владелец: product/engineering
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка Unlimotion
- Ограничения:
  - До явного утверждения SPEC не менять production code, tests, test annotations и поведение продукта.
  - После утверждения разрешены только изменения, прямо нужные для `ST-0011`, `AC-0032`, `AC-0033`, `SC-0011-001`, `SC-0011-002`, `CV-0001`, `CV-0002`.
  - Если реализация требует нового внешнего сервиса, нестабильного инфраструктурного стенда или изменения публичного продуктового контракта, остановиться и вернуть решение владельцу продукта.
  - UI tests не обязательны, пока не меняется UI-facing flow; если в реализации будет затронут Settings/UI flow, применить `AGENTS.override.md` и добавить/запустить релевантные UI tests.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `features/storm/st-0011-server-storage.feature`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/coverage.md`

Если пользователь утверждает эту SPEC фразой `Спеку подтверждаю`, это одновременно подтверждает, что optional server storage является поддерживаемой продуктовой поверхностью, которую нужно довести до automated coverage.

## 1. Overview / Цель
Цель: продолжить `/storm:cover` для `ST-0011` и перевести draft BDD-сценарии server storage в automated coverage без переписывания acceptance criteria и без расширения scope на другие продуктовые поверхности.

Outcome contract:
- Success means:
  - `AC-0032` получает automated evidence для login/register/refresh-token flow.
  - `AC-0033` получает automated evidence для authenticated task CRUD path и выбранной real-time update границы.
  - `SC-0011-001` и `SC-0011-002` получают связи с tests и статус не ниже `automated`; `passing` ставится только после фактического успешного запуска.
  - `CV-0001` и `CV-0002` закрыты или явно снижены с указанием остаточного gap.
- Итоговый артефакт / output:
  - test coverage в `src/Unlimotion.Test`.
  - обновлённые STORM artifacts: `storm.json`, BDD reports, coverage report.
  - validation evidence с командами и результатами.
- Stop rules:
  - Не менять код/тесты до `Спеку подтверждаю`.
  - Остановиться, если выбранный test seam требует live RavenDB/SignalR/server process без стабильной локальной обвязки.
  - Остановиться, если первый characterization test выявляет security/contract bug, который нельзя исправить минимально в рамках `ST-0011` без отдельного product decision.

## 2. Текущее состояние (AS-IS)
- `ST-0011` в `docs/product/storm.json` имеет status `partial`, confidence `0.65`, `linked_tests: []`.
- `AC-0032` описывает login/register/refresh-token flow и сейчас имеет `coverage_level: partial`, test links отсутствуют.
- `AC-0033` описывает authenticated ServiceStack endpoints и SignalR updates; сейчас `coverage_level: partial`, test links отсутствуют.
- BDD layer уже создан:
  - `SC-0011-001` -> `AC-0032`, status `draft`, `automation_status: manual_required_before_automation`.
  - `SC-0011-002` -> `AC-0033`, status `draft`, `automation_status: manual_required_before_automation`.
- Клиентский seam:
  - `src/Unlimotion/ServerStorage.cs` создаёт `JsonServiceClient`, подключает `HubConnection`, вызывает login/register/refresh, выполняет `Save`, `Remove`, `Load`, `GetAll`, `BulkInsert`.
  - `ServerStorage.Connect` пытается использовать refresh token, затем password login, затем fallback register.
  - `ServerStorage.RegisterHandlers` обрабатывает `LogOn`, `ReceiveTaskItem`, `DeleteTaskItem`.
- Серверный auth seam:
  - `src/Unlimotion.Server.ServiceInterface/AuthService.cs` реализует `RegisterNewUser`, `AuthViaPassword`, `PostRefreshToken`, `CreatePassword`, `GetMyProfile`.
  - `src/Unlimotion.Server.ServiceModel/Auth.cs` задаёт ServiceStack DTO/routes для `/register`, `/password/login`, `/token/refresh`, `/me`.
- Серверный task seam:
  - `src/Unlimotion.Server.ServiceInterface/TaskService.cs` реализует authenticated `GetAllTasks`, `GetTask`, `BulkInsertTasks`.
  - `GetAllTasks` фильтрует задачи по `UserId`.
  - `GetTask` сейчас получает задачу по id; при EXEC нужно characterization-проверкой зафиксировать, не нарушает ли это user scope.
  - `BulkInsertTasks` проставляет `UserId` и `TaskItem/` prefix.
- Тестовый проект `src/Unlimotion.Test/Unlimotion.Test.csproj` уже ссылается на `Unlimotion.Server.ServiceInterface` и использует `TUnit`, `JustMock`, `Avalonia.Headless`, `CompareNETObjects`.
- В текущем test inventory нет прямых server storage auth/API/SignalR tests.

## 3. Проблема
`ST-0011` уже описан как продуктовая возможность и имеет BDD-сценарии, но server storage auth/API/real-time behavior не имеет automated evidence. Из-за этого `/storm:cover` не может перевести `SC-0011-001/002` из draft в automated/passing и не может закрыть `CV-0001/CV-0002`.

## 4. Цели дизайна
- Разделение ответственности: auth, task service, client storage и artifact sync проверяются отдельными слоями.
- Повторное использование: использовать существующий `src/Unlimotion.Test` и текущий TUnit style.
- Тестируемость: предпочесть deterministic contract/characterization tests вместо live external infrastructure.
- Консистентность: сохранить STORM trace chain `Story -> AC -> Scenario -> Test -> Code`.
- Обратная совместимость: не менять публичное поведение server storage без отдельного зафиксированного решения.

## 5. Non-Goals (чего НЕ делаем)
- Не запускаем `/storm:full-cycle`.
- Не пересоздаём STORM artifacts.
- Не меняем `ST-0011` acceptance criteria на Gherkin.
- Не добавляем полноценный executable Gherkin runner и реальные `step_definitions`, если это требует отдельной инфраструктуры.
- Не покрываем Telegram bot, attachments, notification/error UX и platform shells.
- Не поднимаем production-like RavenDB/SignalR environment, если для этого нужен нестабильный локальный сервис или внешняя сеть.
- Не меняем UI, если не обнаружится прямая необходимость для server storage Settings flow.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/ServerStorageAuthContractTests.cs` -> automated coverage для `AC-0032`, `SC-0011-001`, `CV-0001`.
- `src/Unlimotion.Test/ServerStorageTaskServiceContractTests.cs` -> automated coverage для `AC-0033`, `SC-0011-002`, `CV-0002`.
- `src/Unlimotion.Test/ServerStorageSignalRContractTests.cs` или секция в task service tests -> минимальная characterization-проверка real-time update boundary, если seam можно стабилизировать без live server.
- `docs/product/storm.json` -> добавить test links к `AC-0032`, `AC-0033`, `ST-0011`, `SC-0011-001`, `SC-0011-002`; обновить coverage/status только по фактическому evidence.
- `docs/product/reports/coverage.md`, `bdd-sync.md`, `bdd-lint.md` -> обновить metrics и remaining gaps после проверки.

### 6.2 Детальный дизайн
- Потоки данных:
  - Auth: `RegisterNewUser` -> `AuthViaPassword` -> `PostRefreshToken` -> `TokenResult`.
  - Client auth: stored `ClientSettings` + `TaskStorageSettings` -> `ServerStorage.Connect` -> bearer token -> `Login`.
  - Task CRUD: authenticated session -> `TaskService.GetAll/GetTask/BulkInsert` -> Raven task documents scoped by user.
  - Real-time boundary: hub event DTO (`ReceiveTaskItem`, `DeleteTaskItem`, `LogOn`) -> `ServerStorage.Updating` / connection state.
- Контракты / API:
  - Access token и refresh token выдаются только валидному пользователю.
  - Refresh token обновляет access token и сохраняет новый refresh token в client settings.
  - Task API не должен возвращать или изменять задачи другого authenticated user.
  - Bulk insert обязан сохранять user scope и task prefix consistently.
  - SignalR update boundary должен доставлять observable update event или иметь явно зафиксированный gap.
- Output contract / evidence rules:
  - Каждый новый test должен быть привязан в STORM к `AC-0032` или `AC-0033`.
  - `SC-0011-001/002` получают `linked_tests` только после добавления тестов.
  - `passing` ставится только после успешного запуска релевантных test commands.
- Visual planning artifact для UI-facing изменений: `Не применимо`, потому что текущая SPEC не меняет UI. Если EXEC затронет Settings UI, остановиться и добавить UI evidence plan.
- UI test video evidence для UI automation задач: `Не применимо` на старте. Если появятся UI changes, применить локальный UI testing override.
- Границы сохранения поведения:
  - Auth/API behavior можно только characterization-покрыть.
  - Любое исправление user isolation или token handling допустимо только если оно минимально, прямо подтверждено failing test и не меняет внешний UX без отдельного решения.
- Обработка ошибок:
  - Ошибки invalid credentials, duplicate register, missing/invalid refresh token должны быть зафиксированы как expected failures/status codes.
  - Сетевые ошибки live server не должны входить в deterministic suite.
- Производительность:
  - Tests должны быть короткими и изолированными; не использовать долгие таймеры/reconnect loops.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Пользователь server storage идентифицируется authenticated session user id.
- Register:
  - новый login создаёт пользователя и secret;
  - duplicate login возвращает error;
  - пустой password возвращает error.
- Password login:
  - валидная пара login/password выдаёт access/refresh token;
  - неверный password или отсутствующий user возвращает error.
- Refresh:
  - refresh token с refresh permission выдаёт новую token pair;
  - invalid/expired refresh token не должен давать access.
- Task API:
  - `GetAllTasks` возвращает только задачи текущего authenticated user.
  - `GetTask` не должен раскрывать задачу другого user.
  - `BulkInsertTasks` сохраняет задачи с `UserId` текущего user и server-side prefix.
- SignalR:
  - `ReceiveTaskItem` транслируется в `TaskStorageUpdateEventArgs` с `UpdateType.Saved`.
  - `DeleteTaskItem` транслируется в `UpdateType.Removed`.

## 8. Точки интеграции и триггеры
- `ServerStorage.Connect` вызывает auth flow и `Login`.
- `ServerStorage.Save/Remove/Load/GetAll/BulkInsert` вызывают hub/API operations.
- `AuthService.Post(RegisterNewUser/AuthViaPassword/PostRefreshToken)` обслуживает auth DTO.
- `TaskService.Get/GetAsync/Post(BulkInsertTasks)` обслуживает task DTO.
- `storm.json` и reports обновляются после успешной validation evidence.

## 9. Изменения модели данных / состояния
- Production persisted model: не планируется менять.
- Test data: создать изолированные users/tasks/tokens для tests.
- STORM state: обновить только traceability fields, coverage metrics, scenario statuses и links.
- `step_definitions`: оставить пустыми, если не вводится реальный BDD runner.

## 10. Миграция / Rollout / Rollback
- Миграция production данных: не применимо, если implementation ограничится tests/artifacts.
- Rollout: добавить tests и artifact updates в обычный PR.
- Rollback: revert test/artifact changes; если будет минимальный production fix, он должен быть отдельным diff block с отдельным rollback note.
- Backward compatibility: существующие local storage и Git backup flows не затрагиваются.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `AC-0032`: tests подтверждают register/login/refresh-token flow или фиксируют конкретный stop gap.
  - `AC-0033`: tests подтверждают authenticated task CRUD/user-scope и real-time boundary или фиксируют конкретный stop gap.
  - `SC-0011-001/002` связаны с tests в `storm.json`.
  - `CV-0001/CV-0002` обновлены в coverage report.
- Какие тесты добавить/изменить:
  - `ServerStorage_LoginRegisterRefreshFlow_ExposesExpectedAuthContracts`
  - `ServerStorage_RefreshToken_RequiresAuthenticatedRefreshRequest`
  - `ServerStorage_Connect_UsesLoginRegisterAndRefreshTokenFlow`
  - `TaskService_TaskEndpoints_RequireAuthenticatedRequests`
  - `TaskService_GetAllAndBulkInsert_PreserveAuthenticatedUserScope`
  - `ServerStorage_SignalRHandlers_MapRemoteTaskUpdatesToStorageEvents`
  - `TaskService_GetTask_DoesNotReturnOtherUsersTask` оставить explicit gap, если deterministic seam без live RavenDB/ServiceStack fixture не подтверждается.
- Characterization tests / contract checks:
  - Проверить текущее поведение `GetTask` по user scope до любых исправлений.
  - Проверить token expiration/custom lifetime boundary без ожидания реального времени, если можно управлять параметрами expiration.
- Visual acceptance для UI-facing изменений: `Не применимо`, UI не меняется.
- UI video evidence для UI-facing фич/багфиксов: `Не применимо` на старте.
- Базовые замеры до/после для performance tradeoff: `Не применимо`, performance behavior не меняется.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter /*/*/*ServerStorage*`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter /*/*/*AuthService*`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
- Stop rules для test/retrieval/tool/validation loops:
  - Не больше двух попыток стабилизировать Raven/ServiceStack test seam без нового решения.
  - Если tests требуют network/live server, остановиться и предложить infrastructure SPEC.
  - Если failing test требует non-trivial production fix, остановиться и оформить отдельную fix SPEC или расширение текущей SPEC на подтверждение.

## 12. Риски и edge cases
- Raven/ServiceStack session mocking может оказаться сложнее, чем direct unit test. Смягчение: сначала выбрать самый простой deterministic seam, не добавлять live dependencies без approval.
- `TaskService.GetTask` может раскрывать cross-user task by id. Смягчение: characterization test должен сделать этот риск явным до исправлений.
- SignalR update path может требовать live `HubConnection`. Смягчение: ограничить coverage boundary event mapping или оставить explicit gap.
- Token generation зависит от configured `JwtAuthProvider`. Смягчение: test fixture должен явно создавать минимальную ServiceStack auth configuration или остановиться.
- UI Settings может быть затронут при client auth flow. Смягчение: если UI меняется, включить UI tests по `AGENTS.override.md`.

## 13. План выполнения
1. Проверить текущий `git status` и зафиксировать, что стартуем от approved SPEC.
2. Выбрать deterministic auth/task test seam: JustMock/fake sessions или локальная in-memory/embedded Raven обвязка без внешней сети.
3. Добавить минимальные auth contract tests для `AC-0032`.
4. Добавить task service contract tests для `AC-0033`, включая user scope.
5. Оценить SignalR boundary: добавить stable test или зафиксировать gap в artifacts.
6. Запустить targeted TUnit commands.
7. Обновить `storm.json`, `bdd-sync.md`, `bdd-lint.md`, `coverage.md`, `traceability.md`.
8. Запустить STORM validator и `git diff --check`.
9. Выполнить Post-EXEC review перед финальным отчётом.

## 14. Открытые вопросы
- Нет блокирующих вопросов до утверждения: approval этой SPEC подтверждает, что `ST-0011` является supported optional product surface.
- Неблокирующий follow-up: нужен ли полноценный executable Gherkin runner для server storage после закрытия coverage gap?

## 15. Соответствие профилю
- Профиль: `storm-product-development`.
- Выполненные требования профиля:
  - Trace chain сохранён: `Story -> AC -> Gherkin Rule -> Gherkin Scenario -> Test -> Code`.
  - Acceptance criteria не заменяются Gherkin.
  - Draft scenarios переводятся в automated только через фактические tests.
  - Code/test changes выносятся из artifact-only sync в отдельную SPEC/QUEST delivery gate.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/ServerStorageBddContractTests.cs` | Новый файл TUnit contract tests | Coverage для `AC-0032`, `AC-0033`, `SC-0011-001`, `SC-0011-002`, `CV-0001`, частично `CV-0002` |
| `src/Unlimotion.Test/Unlimotion.Test.csproj` | Без изменений | Existing ServiceInterface reference достаточно для новых tests |
| `docs/product/storm.json` | Links/status/coverage metrics | Синхронизация Scenario -> Test -> Code |
| `docs/product/reports/bdd-sync.md` | Обновить sync evidence | Отразить BDD status |
| `docs/product/reports/bdd-lint.md` | Обновить lint result | Отразить remaining gaps |
| `docs/product/reports/coverage.md` | Обновить `CV-0001/CV-0002` | Продолжение `/storm:cover` |
| `docs/product/reports/traceability.md` | Обновить trace chain | Видимость Story/Scenario/Test |
| Production code files | Не менять по умолчанию | Только если approved EXEC выявит минимальный required fix и не сработает stop rule |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `ST-0011` | `partial`, `linked_tests: []` | `partial` или `implemented` только по evidence; linked tests добавлены |
| `AC-0032` | `coverage_level: partial`, tests отсутствуют | `critical/full` при успешных auth tests |
| `AC-0033` | `coverage_level: partial`, tests отсутствуют | `critical/full` при успешных task API/real-time boundary tests |
| `SC-0011-001` | `draft`, no linked tests | `automated` или `passing` с linked tests |
| `SC-0011-002` | `draft`, no linked tests | `automated` или `passing` с linked tests |
| `CV-0001/CV-0002` | `proposed` | закрыты, снижены или явно оставлены с reason |

## 18. Альтернативы и компромиссы
- Вариант: Live integration test с реальным server/Raven/SignalR.
  - Плюсы: ближе к production.
  - Минусы: высокая хрупкость, внешняя инфраструктура, медленно.
  - Почему не выбран: `/storm:cover` должен сначала закрыть deterministic contract gaps.
- Вариант: Только artifact update без tests.
  - Плюсы: быстро.
  - Минусы: не переводит draft scenarios в automated и не закрывает coverage.
  - Почему не выбран: уже выполнен BDD sync; следующий gap требует automated evidence.
- Вариант: Полный BDD runner + step definitions.
  - Плюсы: executable specifications.
  - Минусы: отдельная инфраструктура, больше scope.
  - Почему не выбран: текущая цель — закрыть concrete ST-0011 coverage debt.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, проблема, boundaries и non-goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Выбран layered test strategy с rollback/stop rules. |
| C. Безопасность изменений | 11-13 | PASS | Production changes запрещены по умолчанию и ограничены stop rules. |
| D. Проверяемость | 14-16 | PASS | Acceptance, tests и validation commands указаны. |
| E. Готовность к автономной реализации | 17-19 | PARTIAL | Test seam для Raven/ServiceStack нужно подтвердить в EXEC; есть stop rule. |
| F. Соответствие профилю | 20 | PASS | STORM BDD trace и QUEST gate соблюдены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope ограничен `ST-0011/CV-0001/CV-0002`. |
| 2. Понимание текущего состояния | 5 | Зафиксированы текущие code seams и artifact gaps. |
| 3. Конкретность целевого дизайна | 5 | Указаны файлы, tests, trace updates и stop rules. |
| 4. Безопасность (миграция, откат) | 5 | Нет planned migration; rollback понятен. |
| 5. Тестируемость | 4 | Команды и tests заданы; seam может потребовать выбора в EXEC. |
| 6. Готовность к автономной реализации | 4 | Готово при условии соблюдения stop rules для инфраструктуры. |

Итоговый балл: 28 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-14-storm-cover-server-storage-bdd.md`, central template, local `AGENTS.override.md`, `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, related server storage files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: PASS, scope не выходит за `ST-0011`.
  - Contract pass: PASS, acceptance criteria не заменяются Gherkin.
  - Adversarial risk pass: PASS, user-scope/security risk вынесен в characterization и stop rule.
  - Re-review after fixes / Fix and re-review: не требовалось.
  - Stop decision: ждать `Спеку подтверждаю`.
- Evidence inspected:
  - `docs/product/storm.json`
  - `features/storm/st-0011-server-storage.feature`
  - `src/Unlimotion/ServerStorage.cs`
  - `src/Unlimotion.Server.ServiceInterface/AuthService.cs`
  - `src/Unlimotion.Server.ServiceInterface/TaskService.cs`
  - `src/Unlimotion.Server.ServiceModel/Auth.cs`
  - `src/Unlimotion.Server.ServiceModel/Task.cs`
  - `src/Unlimotion.Test/Unlimotion.Test.csproj`
- Depth checklist:
  - Scope drift / unrelated changes: нет.
  - Acceptance criteria: покрыты `AC-0032`, `AC-0033`.
  - Validation evidence: команды указаны, EXEC ещё не выполнялся.
  - Unsupported claims: нет; infrastructure uncertainty вынесена в risks.
  - Regression / edge case: user scope, token expiration и SignalR seam отмечены.
  - Comments/docs/changelog: не требуется до EXEC.
  - Hidden contract change: запрещён stop rules.
  - Manual-review challenge: главный риск — сложность Raven/ServiceStack fixture; остановка задана.
- No-findings justification: SPEC ограничена, проверяема и не разрешает скрыто менять продуктовый контракт.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Raven/ServiceStack test seam может потребовать уточнения в EXEC | Сначала проверить deterministic seam, при нестабильности остановиться | accepted-risk |

- Fixed before continuing: не применимо.
- Checks rerun: чтение template/STORM/code seams.
- Needs human: требуется `Спеку подтверждаю`.
- Residual risks / follow-ups: executable Gherkin runner и full SignalR integration могут потребовать отдельной SPEC.

### Post-EXEC Review
- Статус: PASS с зафиксированными follow-up gaps
- Scope reviewed: approved SPEC, `git status`, `src/Unlimotion.Test/ServerStorageBddContractTests.cs`, `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/bdd-lint.md`, `docs/product/reports/coverage.md`, `docs/product/reports/traceability.md`.
- Decision: завершить текущий `/storm:cover` increment для `ST-0011` как contract-level BDD coverage; не менять production code и не расширять scope на live integration без отдельной SPEC.
- Review passes:
  - Scope/Evidence pass: PASS, изменения ограничены tests/artifacts/spec.
  - Contract pass: PASS, acceptance criteria сохранены и не заменены Gherkin.
  - Adversarial risk pass: PASS, user-scope и live SignalR/RavenDB gaps явно не замаскированы.
  - Re-review after fixes / Fix and re-review: PASS, после исправления using directives targeted tests перезапущены.
  - Stop decision: не продолжать в production code; следующий шаг требует отдельной SPEC.
- Evidence inspected:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed` -> PASS, 6/6.
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release` -> PASS, 0 errors.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build -- --maximum-parallel-tests 1 --output Detailed` -> PASS, 533/533.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` -> PASS, 0 errors, 0 warnings.
  - `git diff --check` -> PASS.
  - `dotnet build src/Unlimotion.sln -c Release` -> BLOCKED by environment workloads: `android` and `wasm-tools` workloads/packages are missing.
- Depth checklist:
  - Scope drift / unrelated changes: не обнаружено в рамках EXEC; untracked STORM artifacts из предыдущего artifact-only sync сохранены.
  - Acceptance criteria: `AC-0032` покрыт critical contract evidence; `AC-0033` покрыт partial/contract evidence.
  - Validation evidence: targeted, project build, full test project, STORM validator и whitespace checks выполнены.
  - Unsupported claims: `passing` поставлен только для сценариев с фактически пройденным `TS-0017`.
  - Regression / edge case: live RavenDB/SignalR и `GetTask` cross-user isolation оставлены explicit gaps.
  - Comments/docs/changelog: changelog не требуется для test/artifact-only increment.
  - Hidden contract change: production code не менялся.
  - Manual-review challenge: один combined contract test file выбран вместо трёх planned files, чтобы не дублировать source-characterization helpers.
- No-findings justification: текущий increment добавляет только deterministic contract/source characterization coverage и корректно оставляет live integration/security follow-up вне scope.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | coverage | `TaskService.GetTask` user-scope остаётся неподтверждённым live/data-level test evidence | Оформить отдельную SPEC для security/contract test seam или production fix после failing characterization | follow-up |
| MEDIUM | coverage | SignalR/RavenDB покрыты contract-level mapping/source evidence, но не live delivery integration | Оформить отдельную SPEC для stable integration harness | follow-up |
| LOW | BDD | `step_definitions` остаются пустыми, executable Gherkin runner не введён | Решать отдельно, если нужен `/storm:bdd-implement` | follow-up |

- Fixed before final report: добавлены недостающие using directives в `ServerStorageBddContractTests.cs`; artifacts синхронизированы с фактическим `TS-0017`.
- Checks rerun: targeted 6/6, test project build, full `Unlimotion.Test` 533/533, STORM validator, `git diff --check`.
- Validation evidence: `SC-0011-001` и `SC-0011-002` переведены в `passing` только после successful targeted run.
- Unrelated changes: untracked `ranking.md`, `stories.md` и feature files других stories относятся к предыдущему BDD/artifact sync и не удалялись.
- Needs human: отдельное подтверждение следующей SPEC для live integration/security gap или `/storm:bdd-implement`.
- Residual risks / follow-ups: full solution build требует установленных Android/iOS/wasm workloads; live server storage integration всё ещё не автоматизирован.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создать отдельную SPEC для продолжения `/storm:cover` по `ST-0011` | 0.84 | Нет approval на EXEC; test seam Raven/ServiceStack подтвердится в реализации | Запросить `Спеку подтверждаю` | Да | Нет | Пользователь сказал "Давай" после предложения SPEC; это не равно утверждению ещё не созданной спеки | `specs/2026-06-14-storm-cover-server-storage-bdd.md` |
| EXEC | Добавить deterministic BDD contract coverage для `ST-0011` | 0.82 | Live RavenDB/SignalR harness не подтверждён; `GetTask` user-scope требует отдельного seam | Обновить STORM artifacts и выполнить validation | Нет | Да, пользователь подтвердил SPEC фразой `Спеку подтверждаю` | Выбран contract/source-characterization слой, потому что он закрывает Scenario -> Test evidence без изменения production behavior | `src/Unlimotion.Test/ServerStorageBddContractTests.cs`, `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature` |
| EXEC | Синхронизировать coverage reports после successful tests | 0.88 | Нет live integration evidence для remaining gaps | Завершить Post-EXEC review и вернуть следующий recommended step | Нет | Да, approval уже получен | Metrics обновлены только на основании фактически пройденного `TS-0017`; gaps оставлены явными | `docs/product/reports/bdd-sync.md`, `docs/product/reports/bdd-lint.md`, `docs/product/reports/coverage.md`, `docs/product/reports/traceability.md`, `specs/2026-06-14-storm-cover-server-storage-bdd.md` |
