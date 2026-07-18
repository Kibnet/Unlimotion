# Исполняемые step definitions для server-storage CRUD и real-time flow

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST SPEC / `/storm:bdd-implement SC-0011-002`
- Владелец: Codex + product owner approval gate
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка `storm-bootstrap`
- Ограничения: product artifacts на русском; production code, `.feature` wording, acceptance criteria и существующие test annotations не менять; EXEC только после фразы `Спеку подтверждаю`
- Связанные ссылки: `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, `src/Unlimotion.Test/ServerStorageBddContractTests.cs`, `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`, `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель

Добавить следующий executable BDD slice для `SC-0011-002`: сценарий "CRUD операций задач выполняется через аутентифицированные ServiceStack endpoints, а SignalR-подключение может доставлять обновления между клиентами" должен исполняться из `.feature` текста через repo-local STORM BDD runner и переиспользовать существующее evidence `TS-0017`, `TS-0018`, `TS-0019`, `TS-0020`.

Outcome contract:
- Success means: `SC-0011-002` получает executable step-definition chain, новый executable spec `TS-0032` проходит, существующие `TS-0017..TS-0020` продолжают проходить.
- Итоговый артефакт / output: test-only BDD slice + синхронизированные `docs/product/storm.json` и reports.
- Stop rules: остановиться, если требуется менять production code, `.feature` wording, acceptance criteria, существующие test annotations, production ServiceStack/RavenDB/SignalR setup, public DTO routes или исправлять unrelated full-suite UI failures.

## 2. Текущее состояние (AS-IS)

- `SC-0011-002` уже есть в `features/storm/st-0011-server-storage.feature`, имеет статус `@passing` и linked tests `TS-0017`, `TS-0018`, `TS-0019`, `TS-0020`.
- `TS-0017` покрывает contract checks: authenticated task endpoints, user-scope source contract and SignalR handler mapping.
- `TS-0018` покрывает security regression for `TaskService.GetTask` user-scope lookup.
- `TS-0019` покрывает live SignalR/RavenDB delivery через repo-local Kestrel host, real ChatHub, real RavenDB services and authenticated clients.
- `TS-0020` покрывает live ServiceStack task API smoke через narrow test-only AppHost, authenticated JsonServiceClient, RavenDB services and cross-user non-leak assertions.
- `SC-0011-001` уже исполняется через `TS-0031` and `SD-0022..SD-0025`.
- Первые три Gherkin steps у `SC-0011-001` и `SC-0011-002` совпадают:
  - `Дано у пользователя открыт актуальный набор задач Unlimotion`
  - `И поведение относится к истории ST-0011`
  - `Когда пользователь использует серверное хранилище`
- Перед SPEC текущие executable BDD slices покрывают 6/45 scenarios; `SC-0011-002` remains the remaining passing server-storage scenario without step definitions.
- Full `Unlimotion.Test` run currently has unrelated UI state/order risk: `MainControlTreeCommandsUiTests.TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` failed in full-suite context and passed in isolation; this SPEC must not fix that unrelated UI test.

## 3. Проблема

`SC-0011-002` имеет passing contract/security/live evidence, но не исполняется как BDD scenario из `.feature` файла. Из-за этого traceability chain для broader server-storage behavior неполная: `Scenario -> Tests` есть, а `Scenario -> Step Definition -> Test contract/live evidence` отсутствует.

## 4. Цели дизайна

- Разделение ответственности: product-level step definitions делегируют в reusable test contracts, не раскрывая ServiceStack/RavenDB mechanics in Gherkin wording.
- Повторное использование: shared server-storage context steps используются для `SC-0011-001` and `SC-0011-002`, а existing test classes keep their method names and annotations.
- Тестируемость: новый `StormServerStorageCrudRealtimeExecutableSpecTests` читает `.feature` файл and verifies exact step ids.
- Консистентность: сохранить repo-local `StormBdd` style, ID sequence and artifact sync pattern.
- Обратная совместимость: не менять production behavior, `.feature` wording, acceptance criteria and existing test annotations.

## 5. Non-Goals

- Не менять production `ServerStorage`, `TaskService`, `ChatHub`, ServiceStack DTOs, auth behavior or public routes.
- Не поднимать production AppHost/license flow and not broaden ServiceStack assembly scanning.
- Не менять `.feature` wording or acceptance criteria.
- Не исправлять unrelated full-suite UI state/order failure in this SPEC.
- Не добавлять Android/iOS environment/setup work.
- Не подключать внешний Cucumber/SpecFlow/BDD framework.
- Не менять existing test annotations.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `src/Unlimotion.Test/ServerStorageAuthContract.cs` -> сохраняет auth-flow contract for `SC-0011-001`.
- `src/Unlimotion.Test/ServerStorageCrudRealtimeContract.cs` -> new reusable test-only contract for `SC-0011-002`:
  - endpoint authentication and user-scope contract checks;
  - SignalR handler mapping contract;
  - optional live SignalR/RavenDB evidence wrapper;
  - optional live ServiceStack task API smoke wrapper.
- `src/Unlimotion.Test/ServerStorageBddContractTests.cs` -> existing `[Test]` methods keep names/annotations and delegate relevant CRUD/security/source checks to reusable contract.
- `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` -> existing `[Test]` methods keep names/annotations and may delegate to reusable live contract helper.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> add/adjust test-only context fields for server-storage CRUD/realtime scenario result.
- `src/Unlimotion.Test/StormBdd/ServerStorageAuthStepDefinitions.cs` or replacement `ServerStorageStepDefinitions.cs` -> reuse `SD-0022..SD-0024` as shared server-storage context steps for `SC-0011-001` and `SC-0011-002`; keep `SD-0025` as auth-specific Then for `SC-0011-001`; add `SD-0026` as CRUD/realtime Then for `SC-0011-002`.
- `src/Unlimotion.Test/StormServerStorageAuthExecutableSpecTests.cs` -> rerun as regression after shared-step refactor.
- `src/Unlimotion.Test/StormServerStorageCrudRealtimeExecutableSpecTests.cs` -> new executable spec `TS-0032`.
- `docs/product/storm.json` and reports -> add `TS-0032`, link `SC-0011-002 -> SD-0022..SD-0024 + SD-0026`, update behavior coverage metrics to 7/45 step-executable scenarios.

### 6.2 Детальный дизайн

Поток:
1. Executable spec парсит `SC-0011-002` из `features/storm/st-0011-server-storage.feature`.
2. Runner executes exact Gherkin steps:
   - `SD-0022`: `Дано у пользователя открыт актуальный набор задач Unlimotion`
   - `SD-0023`: `И поведение относится к истории ST-0011`
   - `SD-0024`: `Когда пользователь использует серверное хранилище`
   - `SD-0026`: `Тогда CRUD операций задач выполняется через аутентифицированные ServiceStack endpoints, а SignalR-подключение может доставлять обновления между клиентами.`
3. `SD-0022..SD-0024` only establish product context and must not hard-code auth-only assertions.
4. `SD-0025` remains auth-specific Then for `SC-0011-001` and invokes `ServerStorageAuthContract`.
5. `SD-0026` invokes `ServerStorageCrudRealtimeContract` and asserts:
   - task endpoints require authenticated requests;
   - GetAll/BulkInsert/GetTask preserve authenticated user scope;
   - SignalR handler mapping supports saved/removed task updates;
   - live SignalR delivery evidence passes;
   - live ServiceStack task API smoke evidence passes.
6. Executable spec verifies scenario passed and executed step ids equal `SD-0022`, `SD-0023`, `SD-0024`, `SD-0026`.

Visual planning artifact для UI-facing изменений: `Не применимо`. Эта SPEC не меняет UI behavior или visual flow.

UI test video evidence для UI automation задач: `Не применимо`. Изменение backend/server-storage test-only BDD, not UI-facing.

Границы сохранения поведения:
- Production behavior не меняется.
- Existing contract/live expectations from `ServerStorageBddContractTests` and `ServerStorageLiveIntegrationTests` сохраняются.
- Existing test method names and annotations сохраняются.
- `.feature` wording сохраняется.

Обработка ошибок:
- If live server/RavenDB/SignalR evidence flakes because of environment/file watcher cleanup, do not change production code; first isolate with targeted `ServerStorageLiveIntegrationTests`.
- If ServiceStack free-quota or AppHost registration requires production setup changes, stop and propose a separate delivery-task.
- If `SC-0011-002` requires different Gherkin wording, stop and propose artifact-only sync SPEC.

Производительность:
- `TS-0032` is heavier than previous BDD slices because it may execute live SignalR/RavenDB and ServiceStack task API smoke. Keep it targeted and do not run it in parallel with other heavy test commands.

## 7. Бизнес-правила / Алгоритмы

- Task CRUD endpoints must require authenticated requests.
- Task data returned through ServiceStack endpoints must be scoped to the authenticated user.
- SignalR updates must deliver task changes to the authenticated user group and avoid sender echo where existing evidence checks it.
- BDD step definitions must stay declarative and product-level; implementation mechanics remain inside reusable test contracts.

## 8. Точки интеграции и триггеры

- Интеграция с `StormFeatureParser` and `StormScenarioRunner`.
- Интеграция с ServiceStack endpoint metadata and `TaskService` source/behavior contracts.
- Интеграция with `ServerStorageLiveIntegrationTests` fixture or extracted helper for live SignalR/RavenDB and ServiceStack API smoke.
- Триггер behavior in executable spec: run `SC-0011-002` from `.feature` text.

## 9. Изменения модели данных / состояния

- Persisted product data не меняется.
- Runtime server/client production state не меняется.
- Test-only temporary RavenDB/Kestrel state may be created by existing live integration fixture and cleaned up by fixture disposal.
- Product artifact state: `storm.json` gets `TS-0032`, `SD-0026`, reused `SD-0022..SD-0024` supports for `SC-0011-002`, scenario links and metrics.

## 10. Миграция / Rollout / Rollback

- Rollout: additive test-only BDD slice, no runtime rollout.
- Rollback: remove new executable spec, new/reused step-definition links and helper extraction; restore existing tests to direct inline assertions if helper extraction is reverted.
- Обратная совместимость: existing `TS-0017..TS-0020` method names, annotations and behavior сохраняются.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
- `SC-0011-002` получает executable step definitions for all four Gherkin steps through shared context steps plus one CRUD/realtime Then step.
- Новый executable spec запускает scenario из `features/storm/st-0011-server-storage.feature`.
- `SC-0011-001` still passes through `TS-0031` after shared-step refactor.
- `ServerStorageBddContractTests` and `ServerStorageLiveIntegrationTests` сохраняют existing test annotations and pass targeted runs.
- `storm.json` and reports reflect `TS-0032`, `SD-0026`, reused `SD-0022..SD-0024` supports and 7/45 step-executable scenarios.
- Production code, `.feature` wording, acceptance criteria and existing test annotations не меняются.

Команды проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageAuthExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-24-storm-bdd-implement-server-storage-crud-realtime-step-definitions.md
```

Full-suite note:
- Full `Unlimotion.Test` remains a known risk from the previous EXEC: one unrelated UI test failed in full-suite context and passed in isolation; sequential full rerun timed out.
- This SPEC may attempt full-suite once after targeted passes if practical, but must not fix unrelated UI tests inside this scope.

Stop rules для validation loops:
- If targeted `ServerStorageLiveIntegrationTests` fails due to environment/file watcher cleanup while the new executable spec fails for the same reason, stop and propose a test-infrastructure stabilization SPEC.
- If targeted live integration fails because of a real server-storage regression caused by the step-helper extraction, fix within test-only scope only if production behavior and existing test annotations remain unchanged.
- If a production fix is needed, stop and propose separate QUEST delivery-task.

## 12. Риски и edge cases

- Риск: `TS-0032` becomes slow or flaky because it reuses live Kestrel/RavenDB evidence.
  - Смягчение: keep targeted validation explicit and avoid parallel heavy test runs.
- Риск: shared `SD-0022..SD-0024` refactor breaks `SC-0011-001`.
  - Смягчение: rerun `StormServerStorageAuthExecutableSpecTests`.
- Риск: helper extraction from live integration tests changes existing test semantics.
  - Смягчение: preserve method names/annotations and rerun both `ServerStorageBddContractTests` and `ServerStorageLiveIntegrationTests`.
- Риск: BDD scenario claims production runtime maturity beyond existing evidence.
  - Смягчение: reports must state this is targeted live integration evidence with narrow test-only ServiceStack registration, not production release smoke.

## 13. План выполнения

1. Refactor shared server-storage context steps so `SD-0022..SD-0024` support both `SC-0011-001` and `SC-0011-002`, with auth execution remaining in `SD-0025`.
2. Extract or add reusable `ServerStorageCrudRealtimeContract` for endpoint auth/user-scope, SignalR mapping, live SignalR delivery and live ServiceStack task API smoke.
3. Keep existing `ServerStorageBddContractTests` and `ServerStorageLiveIntegrationTests` methods and annotations, delegating to reusable contract where needed.
4. Add `SD-0026` for `SC-0011-002` Then outcome.
5. Add `StormServerStorageCrudRealtimeExecutableSpecTests` as `TS-0032`.
6. Run build and targeted TUnit commands.
7. Run `/storm:bdd-sync` and `/storm:bdd-lint` artifact updates for new links and metrics.
8. Run artifact validation and hygiene checks.
9. Perform post-EXEC review and document full-suite risk separately if it persists.

## 14. Открытые вопросы

Блокирующих вопросов нет. Known full-suite UI risk is explicitly outside this SPEC.

## 15. Соответствие профилю

- Профиль: `storm-product-development` + `delivery-task` через QUEST.
- Выполненные требования профиля:
  - Gherkin не заменяет acceptance criteria.
  - `Scenario -> Test -> Step Definition -> Code` будет синхронизирован.
  - `/storm:bdd-implement` не стартует без SPEC approval.
  - Product artifacts остаются на русском.
  - Production code, `.feature` wording and existing test annotations stay unchanged.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/ServerStorageCrudRealtimeContract.cs` | new reusable contract/helper | Переиспользовать existing TS-0017..TS-0020 evidence в BDD step definitions |
| `src/Unlimotion.Test/ServerStorageBddContractTests.cs` | delegate relevant contract checks if needed | Сохранить TS-0017/TS-0018 and avoid duplication |
| `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` | delegate live evidence if needed | Сохранить TS-0019/TS-0020 and avoid duplication |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | context fields/result | Передать CRUD/realtime result between Gherkin steps |
| `src/Unlimotion.Test/StormBdd/ServerStorageAuthStepDefinitions.cs` or `ServerStorageStepDefinitions.cs` | shared `SD-0022..SD-0024`, `SD-0026` | Исполнить `SC-0011-002` without duplicate context steps |
| `src/Unlimotion.Test/StormServerStorageAuthExecutableSpecTests.cs` | regression run if runner class/step factory changes | Preserve `SC-0011-001` executable slice |
| `src/Unlimotion.Test/StormServerStorageCrudRealtimeExecutableSpecTests.cs` | new executable spec `TS-0032` | Запуск `SC-0011-002` из `.feature` |
| `docs/product/storm.json` | traceability and metrics sync | `TS-0032`, `SD-0026`, reused `SD-0022..SD-0024`, 7/45 |
| `docs/product/reports/*.md` | coverage/sync/lint/rank/trace/stories updates | Отразить BDD evidence |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| `SC-0011-002.step_definitions` | `[]` | `SD-0022`, `SD-0023`, `SD-0024`, `SD-0026` |
| `SC-0011-002.linked_tests` | `TS-0017`, `TS-0018`, `TS-0019`, `TS-0020` | `TS-0017`, `TS-0018`, `TS-0019`, `TS-0020`, `TS-0032` |
| Step-executable scenarios | 6/45 | 7/45 |
| `SD-0022..SD-0024.supports_scenarios` | `SC-0011-001` | `SC-0011-001`, `SC-0011-002` |
| `SC-0011-001` | step-executable | remains step-executable |

## 18. Альтернативы и компромиссы

- Вариант A: duplicate all four step definitions for `SC-0011-002`.
  - Плюсы: minimal refactor.
  - Минусы: increases duplicate step-text warnings and hides shared server-storage context.
- Вариант B: reuse `SD-0022..SD-0024` as shared context and add only `SD-0026` for the new Then outcome.
  - Плюсы: lower duplicate-step noise, clearer shared context, preserves Gherkin wording.
  - Минусы: requires small refactor of existing auth step flow and regression run for `TS-0031`.
- Вариант C: skip live evidence in executable BDD and use only source/contract checks.
  - Плюсы: faster and less flaky.
  - Минусы: weaker traceability for `SC-0011-002`, whose value includes SignalR and ServiceStack API behavior.
- Выбран Вариант B, потому что он keeps BDD layer cleaner and preserves already existing live evidence.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели and non-goals are explicit. |
| B. Качество дизайна | 6-10 | PASS | Responsibility split, step reuse, integration points, rollback and state impact described. |
| C. Безопасность изменений | 11-13 | PASS | Stop rules protect production code, feature wording, annotations and unrelated full-suite UI risk. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria and targeted TUnit commands are explicit. |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan, alternatives and review complete; no blocking questions. |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST route and Russian product artifacts respected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope is one existing passing scenario and explicitly excludes production/server setup changes. |
| 2. Понимание текущего состояния | 5 | Сверены feature, storm artifacts, reports, contract tests and live integration tests. |
| 3. Конкретность целевого дизайна | 5 | Planned files, IDs, shared-step strategy and validation commands are explicit. |
| 4. Безопасность (миграция, откат) | 5 | Rollback and stop rules preserve production behavior and existing annotations. |
| 5. Тестируемость | 5 | Includes build, new executable spec, existing auth spec, contract suite, live integration suite and artifact validator. |
| 6. Готовность к автономной реализации | 5 | No blocking questions; known full-suite risk is isolated and documented. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-24-storm-bdd-implement-server-storage-crud-realtime-step-definitions.md`; instruction stack: central `AGENTS.md`, local `AGENTS.override.md`, `storm-product-development`, QUEST SPEC gate; planned changed files in section 16.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: inspected `features/storm/st-0011-server-storage.feature`, `docs/product/storm.json`, current reports, `ServerStorageBddContractTests.cs`, `ServerStorageLiveIntegrationTests.cs`.
  - Contract pass: no production code, no `.feature` wording, no acceptance criteria changes, no existing test annotation changes.
  - Adversarial risk pass: checked live infrastructure flakiness, ServiceStack AppHost/license scope creep, false runtime release claims and unrelated UI full-suite failure.
  - Re-review after fixes / Fix and re-review: no fixes needed.
  - Stop decision: PASS, approval can be requested.
- Evidence inspected:
  - `features/storm/st-0011-server-storage.feature`
  - `src/Unlimotion.Test/ServerStorageBddContractTests.cs`
  - `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/ranking.md`
  - `docs/product/storm.json`
- Depth checklist:
  - Scope drift / unrelated changes: blocked by Non-Goals and Stop rules.
  - Acceptance criteria: `AC-0033` is preserved and executable evidence will be additive.
  - Validation evidence: commands listed.
  - Unsupported claims: production runtime/release maturity not claimed.
  - Regression / edge case: `TS-0031`, `TS-0017..TS-0020` reruns required.
  - Comments/docs/changelog: no changelog required for test-only local slice.
  - Hidden contract change: shared steps refactor is constrained by rerunning `TS-0031`.
  - Manual-review challenge: reviewer may ask why live integration belongs in BDD; answer is `SC-0011-002` explicitly includes SignalR delivery and ServiceStack endpoint behavior, already backed by TS-0019/TS-0020.
- No-findings justification: SPEC is an additive BDD executable slice using existing evidence, with explicit stop rules for production and infrastructure changes.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | New executable spec may be heavier/flakier because it uses live integration evidence. | Run targeted live suite and stop on infra failure instead of changing production. | accepted-risk |

- Fixed before continuing: not required.
- Checks rerun: manual spec-linter/spec-rubric/post-SPEC review.
- Needs human: approval to move from SPEC to EXEC.
- Residual risks / follow-ups: unrelated full-suite UI state/order failure remains separate; Android/iOS `NETSDK1147` remains separate.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved `/storm:bdd-implement SC-0011-002` delivery-task; changed only test code/helpers, STORM artifacts and reports.
- Decision: завершить EXEC и запросить следующий процессный шаг без перехода к новым code/test изменениям.
- Review passes:
  - Scope/Evidence pass: `SC-0011-002` теперь исполняется из feature text через `TS-0032`; existing `TS-0017..TS-0020` evidence сохранено and rerun.
  - Contract pass: production code, `.feature` wording, acceptance criteria and existing test annotations не менялись.
  - Adversarial risk pass: live SignalR/ServiceStack evidence rerun targeted; production AppHost/license setup не менялся; unrelated full-suite UI risk not fixed in this scope.
  - Re-review after fixes / Fix and re-review: build initially blocked by stale `Unlimotion.Test (33028)` output lock; stale test host stopped, build rerun passed.
  - Stop decision: PASS; no stop-rule violation.
- Evidence inspected:
  - `src/Unlimotion.Test/ServerStorageCrudRealtimeContract.cs`
  - `src/Unlimotion.Test/StormServerStorageCrudRealtimeExecutableSpecTests.cs`
  - `src/Unlimotion.Test/StormBdd/ServerStorageAuthStepDefinitions.cs`
  - `src/Unlimotion.Test/ServerStorageBddContractTests.cs`
  - `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`
  - `docs/product/storm.json`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/coverage.md`
- Depth checklist:
  - Scope drift / unrelated changes: no production changes; no `.feature` changes; no test annotation changes.
  - Acceptance criteria: `AC-0033` preserved and linked to `TS-0032`.
  - Validation evidence: build, new executable spec, auth regression spec, contract suite and live integration suite passed.
  - Unsupported claims: reports state targeted live integration evidence; no runtime/release production maturity claim.
  - Regression / edge case: `TS-0031` auth executable spec passed after shared-step refactor.
  - Comments/docs/changelog: no changelog needed for test-only/product-artifact BDD sync.
  - Hidden contract change: existing `ServerStorageBddContractTests` and `ServerStorageLiveIntegrationTests` method names/annotations preserved while delegating to reusable contract.
  - Manual-review challenge: live evidence is intentionally inside `TS-0032` because `SC-0011-002` observable outcome includes both authenticated ServiceStack task API and SignalR delivery.
- No-findings justification: targeted evidence covers the agreed BDD slice; residual risks are separate known tracks.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation environment | First build attempt failed because stale `Unlimotion.Test (33028)` locked output DLLs. | Stop stale test host and rerun build. | resolved |
| LOW | full-suite risk | Previous unrelated full-suite UI state/order risk remains out of scope. | Handle through separate stabilization SPEC if needed. | accepted-risk |

- Fixed before final report: build output lock resolved by stopping stale `Unlimotion.Test (33028)`.
- Checks rerun:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageAuthExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - `rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-24-storm-bdd-implement-server-storage-crud-realtime-step-definitions.md`
- Validation evidence:
  - build passed after stale test host cleanup.
  - build rerun passed after adding `NotInParallel("ServerStorageLiveIntegration")` to the new executable spec.
  - `StormServerStorageCrudRealtimeExecutableSpecTests` passed 1/1.
  - `StormServerStorageCrudRealtimeExecutableSpecTests` rerun passed 1/1 after `NotInParallel` annotation.
  - `StormServerStorageAuthExecutableSpecTests` passed 1/1.
  - `ServerStorageBddContractTests` passed 7/7.
  - `ServerStorageLiveIntegrationTests` passed 2/2.
  - `validate-artifacts.py` returned 0 errors and 1 known warning for intentional shared Given step text across `SD-0009`, `SD-0013` and `SD-0022`.
  - `git diff --check` passed with only LF-to-CRLF working-copy warnings.
  - trailing whitespace search returned no matches.
- Unrelated changes: no unrelated worktree changes were modified.
- Needs human: no for current EXEC; new tests/code require a new approved SPEC.
- Residual risks / follow-ups: separately consider full-suite stabilization or Android/iOS environment setup.

## Approval

Получено: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Проверка состояния после коммита | 0.98 | Нет | Выбрать следующий SPEC-кандидат | Нет | Нет | Working tree clean after `6696bd9`; reports recommend `SC-0011-002`. | `git status`, `git log`, `docs/product/reports/*.md` |
| SPEC | Выбор следующего BDD slice | 0.88 | Нет | Создать SPEC для `SC-0011-002` | Нет | Нет | `SC-0011-002` is the remaining passing server-storage scenario without step definitions and has TS-0017..TS-0020 evidence. | `features/storm/st-0011-server-storage.feature`, `src/Unlimotion.Test/ServerStorageBddContractTests.cs`, `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` |
| SPEC | Подготовка SPEC и review | 0.9 | Нет | Запросить подтверждение пользователя | Да | Нет | `/storm:bdd-implement` changes tests/artifacts, so QUEST gate is required. | `specs/2026-06-24-storm-bdd-implement-server-storage-crud-realtime-step-definitions.md` |
| EXEC | Реализация reusable CRUD/SignalR contract | 0.86 | Нет | Подключить step definitions | Нет | Да: пользователь подтвердил SPEC | Existing TS-0017..TS-0020 evidence reused without changing annotations or production code. | `src/Unlimotion.Test/ServerStorageCrudRealtimeContract.cs`, `src/Unlimotion.Test/ServerStorageBddContractTests.cs`, `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` |
| EXEC | Добавление `TS-0032` и `SD-0026` | 0.9 | Нет | Запустить targeted validation | Нет | Да: пользователь подтвердил SPEC | `SC-0011-002` now executes existing feature text through shared context steps plus CRUD/realtime Then step. | `src/Unlimotion.Test/StormServerStorageCrudRealtimeExecutableSpecTests.cs`, `src/Unlimotion.Test/StormBdd/ServerStorageAuthStepDefinitions.cs`, `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` |
| EXEC | Validation | 0.9 | Нет | Sync artifacts | Нет | Да: пользователь подтвердил SPEC | Build and targeted TUnit suites passed after resolving stale test-host output lock. | build, `StormServerStorageCrudRealtimeExecutableSpecTests`, `StormServerStorageAuthExecutableSpecTests`, `ServerStorageBddContractTests`, `ServerStorageLiveIntegrationTests` |
| EXEC | `/storm:bdd-sync` и `/storm:bdd-lint` artifact sync | 0.88 | Нет | Финальный hygiene/review | Нет | Да: пользователь подтвердил SPEC | `storm.json` and reports now record `TS-0032`, `SD-0026`, 7/45 step-executable scenarios and remaining gaps. | `docs/product/storm.json`, `docs/product/reports/*.md` |
| EXEC | Artifact validation и hygiene | 0.92 | Нет | Завершить EXEC | Нет | Да: пользователь подтвердил SPEC | STORM validator passed with 0 errors and the known duplicate Given warning; diff and trailing whitespace hygiene passed. | `docs/product/storm.json`, `docs/product/reports/*.md`, `specs/2026-06-24-storm-bdd-implement-server-storage-crud-realtime-step-definitions.md` |
| EXEC | Self-review concurrency guard | 0.86 | Нет | Повторить build и `TS-0032` | Нет | Да: пользователь подтвердил SPEC | New executable spec runs live evidence, so it now joins the existing `ServerStorageLiveIntegration` non-parallel group. | `src/Unlimotion.Test/StormServerStorageCrudRealtimeExecutableSpecTests.cs` |
