# Исполняемые step definitions для server-storage auth flow

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST SPEC / `/storm:bdd-implement SC-0011-001`
- Владелец: Codex + product owner approval gate
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка `storm-bootstrap`
- Ограничения: product artifacts на русском; production code, `.feature` wording, acceptance criteria и существующие test annotations не менять; EXEC только после фразы `Спеку подтверждаю`
- Связанные ссылки: `docs/product/storm.json`, `features/storm/st-0011-server-storage.feature`, `src/Unlimotion.Test/ServerStorageBddContractTests.cs`, `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель

Добавить следующий узкий executable BDD slice для `SC-0011-001`: сценарий "Клиент поддерживает login/register/refresh-token flow для серверного хранилища" должен исполняться из `.feature` текста через repo-local STORM BDD runner и переиспользовать существующее contract evidence `TS-0017`.

Outcome contract:
- Success means: `SC-0011-001` получает `SD-0022..SD-0025`, новый executable spec `TS-0031` проходит, существующий `TS-0017` продолжает проходить.
- Итоговый артефакт / output: test-only BDD slice + синхронизированные `docs/product/storm.json` и reports.
- Stop rules: остановиться, если требуется менять production code, `.feature` wording, acceptance criteria, существующие test annotations, runtime server behavior, live RavenDB/SignalR/ServiceStack setup или переходить в `SC-0011-002`.

## 2. Текущее состояние (AS-IS)

- `SC-0011-001` уже есть в `features/storm/st-0011-server-storage.feature`, имеет статус `@passing` и linked test `TS-0017`.
- `TS-0017` указывает на `ServerStorageBddContractTests` и содержит passing contract checks для server-storage auth flow.
- Для `SC-0011-001` важны три существующих checks внутри `TS-0017`:
  - `ServerStorage_LoginRegisterRefreshFlow_ExposesExpectedAuthContracts`
  - `ServerStorage_RefreshToken_RequiresAuthenticatedRefreshRequest`
  - `ServerStorage_Connect_UsesLoginRegisterAndRefreshTokenFlow`
- Эти checks подтверждают routes `/password/login`, `/register`, `/token/refresh`, authenticated refresh-token endpoint и client-side usage of login/register/refresh-token.
- `SC-0011-002` покрывает более широкий CRUD/SignalR scope и связан с `TS-0017..TS-0020`; он намеренно не входит в этот SPEC.
- Перед SPEC текущие executable BDD slices покрывают 5/45 scenarios; `SC-0011-001` и `SC-0011-002` остаются passing scenarios без step definitions.

## 3. Проблема

`SC-0011-001` покрыт contract test evidence, но не исполняется как BDD scenario из `.feature` файла. Из-за этого traceability chain неполная: `Scenario -> Test` есть, а `Scenario -> Step Definition -> Test contract` отсутствует.

## 4. Цели дизайна

- Разделение ответственности: contract checks остаются в test layer, step definitions только связывают Gherkin wording с reusable assertions.
- Повторное использование: existing `ServerStorageBddContractTests` и новый executable spec используют один auth-flow contract без дублирования логики.
- Тестируемость: новый `StormServerStorageAuthExecutableSpecTests` читает `.feature` файл и проверяет exact step ids.
- Консистентность: сохранить repo-local `StormBdd` style, ID sequence and artifact sync pattern.
- Обратная совместимость: не менять production behavior, `.feature` wording, acceptance criteria и existing test annotations.

## 5. Non-Goals

- Не реализовывать `SC-0011-002` CRUD/SignalR executable slice.
- Не запускать live RavenDB, SignalR hub, Kestrel или real ServiceStack HTTP integration.
- Не менять `ServerStorage`, `TaskService`, ServiceStack DTOs или auth behavior.
- Не расширять server-storage product scope и не менять maturity/status claims.
- Не заменять acceptance criteria на Gherkin.
- Не подключать внешний Cucumber/SpecFlow/BDD framework.
- Не менять test annotations existing tests.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `src/Unlimotion.Test/ServerStorageAuthContract.cs` -> reusable contract assertions for login/register/refresh-token flow.
- `src/Unlimotion.Test/ServerStorageBddContractTests.cs` -> сохраняет existing `[Test]` methods and delegates auth-flow checks to reusable contract.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> добавляет минимальное состояние контекста для server-storage auth scenario, если текущего контекста недостаточно.
- `src/Unlimotion.Test/StormBdd/ServerStorageAuthStepDefinitions.cs` -> регистрирует `SD-0022..SD-0025` для четырех шагов `SC-0011-001`.
- `src/Unlimotion.Test/StormServerStorageAuthExecutableSpecTests.cs` -> парсит `features/storm/st-0011-server-storage.feature`, исполняет `SC-0011-001` через runner и проверяет executed step ids.
- `docs/product/storm.json` и reports -> добавляют `TS-0031`, `SD-0022..SD-0025`, обновляют behavior coverage metrics до 6/45 step-executable scenarios.

### 6.2 Детальный дизайн

Поток:
1. Executable spec парсит `SC-0011-001` из `features/storm/st-0011-server-storage.feature`.
2. Runner исполняет exact Gherkin steps:
   - `SD-0022`: `Дано у пользователя открыт актуальный набор задач Unlimotion`
   - `SD-0023`: `И поведение относится к истории ST-0011`
   - `SD-0024`: `Когда пользователь использует серверное хранилище`
   - `SD-0025`: `Тогда Клиент поддерживает login/register/refresh-token flow для серверного хранилища.`
3. Given/And/When steps устанавливают product context only: active task set, story id, server-storage surface.
4. Then step вызывает reusable auth-flow contract, который проверяет:
   - ServiceStack routes для login/register/refresh-token;
   - refresh-token endpoint требует authenticated request;
   - client code использует login/register/refresh-token path and persists refreshed token.
5. Executable spec проверяет, что scenario passed and executed step ids equal `SD-0022..SD-0025`.

Visual planning artifact для UI-facing изменений: `Не применимо`. Эта SPEC не меняет UI behavior или visual flow.

UI test video evidence для UI automation задач: `Не применимо`. Изменение backend-contract/test-only BDD, не UI-facing.

Границы сохранения поведения:
- Production behavior не меняется.
- Existing contract expectations из `ServerStorageBddContractTests` сохраняются.
- Existing test method names and annotations сохраняются.
- `.feature` wording сохраняется.

Обработка ошибок:
- Если feature parser не находит `SC-0011-001`, executable spec падает.
- Если route/auth/source contract drift happens, both existing `TS-0017` and new `TS-0031` must fail.

Производительность:
- Contract checks reflection/source based; no external services. Expected runtime stays small.

## 7. Бизнес-правила / Алгоритмы

- Server-storage auth surface must expose login, register and refresh-token request contracts.
- Refresh-token request must be protected by authentication.
- Client connect flow must use login/register/refresh-token and persist refreshed token.
- BDD scenario must stay at product-behavior level; code-level assertions remain inside reusable contract.

## 8. Точки интеграции и триггеры

- Интеграция с `StormFeatureParser` and `StormScenarioRunner`.
- Интеграция с ServiceStack metadata через reflection on DTO route attributes and service method attributes.
- Интеграция с source-level contract checks for `src/Unlimotion/ServerStorage.cs`.
- Триггер behavior in executable spec: run `SC-0011-001` from `.feature` text.

## 9. Изменения модели данных / состояния

- Persisted product data не меняется.
- Runtime server/client state не меняется.
- Test-only state: `StormScenarioContext` может получить поля for active story/server-storage context and auth-flow result.
- Product artifact state: `storm.json` получает новые `TS-0031`, `SD-0022..SD-0025`, scenario links and metrics.

## 10. Миграция / Rollout / Rollback

- Rollout: additive test-only BDD slice, не влияет на runtime.
- Rollback: удалить новый executable spec, step definitions and helper extraction; вернуть `ServerStorageBddContractTests` inline checks if helper was extracted.
- Обратная совместимость: existing `TS-0017` test method names, annotations and assertions сохраняются.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
- `SC-0011-001` получает исполняемые step definitions для всех четырех Gherkin steps.
- Новый executable spec запускает scenario из `features/storm/st-0011-server-storage.feature`.
- `ServerStorageBddContractTests` сохраняет existing test annotation attributes and passes.
- `storm.json` и reports отражают `TS-0031`, `SD-0022..SD-0025` и 6/45 step-executable scenarios.
- Production code, `.feature` wording, acceptance criteria and existing test annotations не меняются.
- `SC-0011-002` remains unchanged and is not claimed as step-executable.

Команды проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageAuthExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-23-storm-bdd-implement-server-storage-auth-step-definitions.md
```

Stop rules для validation loops:
- Если auth-flow execution требует live server/RavenDB/SignalR setup, остановиться и предложить отдельный SPEC для `SC-0011-002` или integration evidence.
- Если scenario execution требует `.feature` wording changes, остановиться и предложить отдельный artifact-sync SPEC.
- Если обнаружится production auth bug, остановиться и предложить отдельный delivery-task through QUEST before changing production code.

## 12. Риски и edge cases

- Риск: executable BDD slice начнет claiming live server behavior.
  - Смягчение: scenario scope limited to contract-level auth flow and uses existing `TS-0017` evidence only.
- Риск: helper extraction accidentally changes existing test annotations.
  - Смягчение: annotations are out of scope; only method bodies may delegate to helper.
- Риск: `SC-0011-002` CRUD/SignalR assertions leak into auth-only slice.
  - Смягчение: keep `TS-0018..TS-0020` and CRUD/SignalR step definitions out of this SPEC.
- Риск: source-level assertions are brittle.
  - Смягчение: this preserves current contract-test approach; improving runtime-level coverage is a separate SPEC.

## 13. План выполнения

1. Extract reusable auth-flow checks from the first three `ServerStorageBddContractTests` methods into `ServerStorageAuthContract`.
2. Keep existing `ServerStorageBddContractTests` methods and annotations, delegating to the contract.
3. Add context/result fields for server-storage auth scenario if current `StormScenarioContext` does not already support them.
4. Add `ServerStorageAuthStepDefinitions` with `SD-0022..SD-0025`.
5. Add `StormServerStorageAuthExecutableSpecTests` as new `TS-0031`.
6. Run build, targeted executable spec and existing `ServerStorageBddContractTests`.
7. Run `/storm:bdd-sync` and `/storm:bdd-lint` artifact-only updates for the new links and metrics.
8. Run artifact validation and hygiene checks.

## 14. Открытые вопросы

Блокирующих вопросов нет. Эта SPEC intentionally chooses `SC-0011-001`; `SC-0011-002` remains a separate candidate because it needs broader CRUD/SignalR evidence.

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
| `src/Unlimotion.Test/ServerStorageAuthContract.cs` | new reusable contract helper | Переиспользовать existing TS-0017 auth-flow evidence в BDD step definitions |
| `src/Unlimotion.Test/ServerStorageBddContractTests.cs` | delegate existing auth-flow test bodies to contract | Сохранить TS-0017 и убрать дублирование |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | context fields/result if needed | Передать auth-flow result between Gherkin steps |
| `src/Unlimotion.Test/StormBdd/ServerStorageAuthStepDefinitions.cs` | `SD-0022..SD-0025` | Исполнить `SC-0011-001` |
| `src/Unlimotion.Test/StormServerStorageAuthExecutableSpecTests.cs` | new executable spec `TS-0031` | Запуск scenario из `.feature` |
| `docs/product/storm.json` | traceability and metrics sync | `TS-0031`, `SD-0022..SD-0025`, 6/45 |
| `docs/product/reports/*.md` | coverage/sync/lint/rank/trace/stories updates | Отразить BDD evidence |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| `SC-0011-001.step_definitions` | `[]` | `SD-0022..SD-0025` |
| `SC-0011-001.linked_tests` | `TS-0017` | `TS-0017`, `TS-0031` |
| Step-executable scenarios | 5/45 | 6/45 |
| Existing auth contract test | inline assertions | same test methods delegate to reusable contract |
| `SC-0011-002` | passing without step definitions | unchanged |

## 18. Альтернативы и компромиссы

- Вариант A: реализовать `SC-0011-001` auth-flow executable slice.
  - Плюсы: narrow backend-contract slice, no live infrastructure, directly follows current ranking candidate.
  - Минусы: remains contract-level, not live HTTP behavior.
- Вариант B: реализовать `SC-0011-002` CRUD/SignalR executable slice.
  - Плюсы: closes broader server-storage scenario.
  - Минусы: higher blast radius, touches live RavenDB/SignalR/ServiceStack integration evidence.
- Вариант C: stop at current 5/45 and switch to Android/iOS environment blocker.
  - Плюсы: addresses build-smoke gap.
  - Минусы: does not continue BDD executable coverage.
- Выбран Вариант A, потому что пользователь подтвердил продолжение `/storm:cover`, а `SC-0011-001` is the smallest remaining passing scenario without step definitions.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals конкретны. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, flow, integration points, data impact and rollback described. |
| C. Безопасность изменений | 11-13 | PASS | Stop rules, rollback and production/server behavior boundaries fixed. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, commands and file table present. |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan, alternatives and review complete; no blocking questions. |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST route and Russian product artifacts respected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope narrow: one scenario, one existing auth-flow contract slice. |
| 2. Понимание текущего состояния | 5 | Сверены feature, storm.json reports and `ServerStorageBddContractTests`. |
| 3. Конкретность целевого дизайна | 5 | Planned files, IDs, flow and validation commands explicit. |
| 4. Безопасность (миграция, откат) | 5 | Production/server behavior unchanged; rollback path listed. |
| 5. Тестируемость | 5 | Build, targeted executable spec, existing contract test and artifact validator listed. |
| 6. Готовность к автономной реализации | 5 | No blocking questions; stop conditions concrete. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-23-storm-bdd-implement-server-storage-auth-step-definitions.md`; instruction stack: central `AGENTS.md`, local `AGENTS.override.md`, `storm-product-development`, QUEST SPEC gate; planned changed files in section 16.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: inspected `features/storm/st-0011-server-storage.feature`, `docs/product/storm.json`, current reports and `ServerStorageBddContractTests.cs`.
  - Contract pass: no production code, no `.feature` wording, no acceptance criteria changes, no existing test annotation changes.
  - Adversarial risk pass: checked scope creep into `SC-0011-002`, live infrastructure, production auth fixes and runtime maturity claims.
  - Re-review after fixes / Fix and re-review: no fixes needed.
  - Stop decision: PASS, approval can be requested.
- Evidence inspected:
  - `features/storm/st-0011-server-storage.feature`
  - `src/Unlimotion.Test/ServerStorageBddContractTests.cs`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/ranking.md`
  - `docs/product/storm.json`
- Depth checklist:
  - Scope drift / unrelated changes: blocked by Non-Goals and Stop rules.
  - Acceptance criteria: scenario AC is preserved and executable evidence will be additive.
  - Validation evidence: commands listed.
  - Unsupported claims: live server behavior and `SC-0011-002` are explicitly not claimed.
  - Regression / edge case: existing `TS-0017` rerun required.
  - Comments/docs/changelog: no changelog required for test-only local slice.
  - Hidden contract change: none planned; helper extraction preserves assertions.
  - Manual-review challenge: reviewer may ask why not implement `SC-0011-002`; answer is blast-radius control and separate live-integration scope.
- No-findings justification: SPEC is a narrow additive BDD executable slice using existing contract evidence, with no unresolved product or architecture choice.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Auth-flow evidence remains contract/source-level, not live HTTP. | Keep scope explicit; live HTTP belongs to `SC-0011-002` or separate SPEC. | accepted-risk |

- Fixed before continuing: not required.
- Checks rerun: manual spec-linter/spec-rubric/post-SPEC review.
- Needs human: approval to move from SPEC to EXEC.
- Residual risks / follow-ups: `SC-0011-002` remains passing without step definitions and should be separate after auth slice.

### Post-EXEC Review
- Статус: Не выполнен до EXEC
- Scope reviewed: Не применимо до утверждения спеки.
- Decision: Не применимо до EXEC.
- Review passes:
  - Scope/Evidence pass: Не выполнен до EXEC.
  - Contract pass: Не выполнен до EXEC.
  - Adversarial risk pass: Не выполнен до EXEC.
  - Re-review after fixes / Fix and re-review: Не выполнен до EXEC.
  - Stop decision: Ждать фразу `Спеку подтверждаю`.
- Evidence inspected: Не применимо до EXEC.
- Depth checklist:
  - Scope drift / unrelated changes: Не применимо до EXEC.
  - Acceptance criteria: Не применимо до EXEC.
  - Validation evidence: Не применимо до EXEC.
  - Unsupported claims: Не применимо до EXEC.
  - Regression / edge case: Не применимо до EXEC.
  - Comments/docs/changelog: Не применимо до EXEC.
  - Hidden contract change: Не применимо до EXEC.
  - Manual-review challenge: Не применимо до EXEC.
- No-findings justification: EXEC еще не начинался.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| BLOCKER/HIGH/MEDIUM/LOW | spec compliance / regression / tests / docs / comments / unrelated changes / evidence / follow-up | Нет находок до EXEC | Ждать approval | ask-human |

- Fixed before final report: Не применимо.
- Checks rerun: Не применимо.
- Validation evidence: Не применимо.
- Unrelated changes: Не применимо.
- Needs human: approval phrase.
- Residual risks / follow-ups: После `SC-0011-001` отдельно рассмотреть `SC-0011-002`.

## Approval

Ожидается фраза: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Проверка состояния перед продолжением | 0.95 | Нет | Подготовить следующий SPEC | Нет | Нет | Working tree clean; предыдущий slice уже закоммичен как `15e2a25`. | `git status`, `git log` |
| SPEC | Выбор следующего BDD slice | 0.9 | Нет | Создать SPEC для `SC-0011-001` | Нет | Нет | `SC-0011-001` является smallest remaining passing scenario без step definitions and has existing contract evidence `TS-0017`. | `features/storm/st-0011-server-storage.feature`, `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`, `src/Unlimotion.Test/ServerStorageBddContractTests.cs` |
| SPEC | Подготовка SPEC и review | 0.92 | Нет | Запросить подтверждение пользователя | Да | Нет | `/storm:bdd-implement` меняет tests/artifacts, поэтому нужен QUEST gate. | `specs/2026-06-23-storm-bdd-implement-server-storage-auth-step-definitions.md` |
