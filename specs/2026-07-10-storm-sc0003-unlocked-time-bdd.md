# STORM SC-0003-002: executable BDD для UnlockedDateTime

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая ветка `storm-bootstrap`
- Ограничения: не менять product behavior; не менять acceptance criteria; не менять `.feature` wording; не менять existing test annotations; не удалять stories/tests/conflicts/dependencies; продуктовые артефакты вести на русском
- Связанные ссылки: `docs/product/storm.json`, `docs/product/reports/*`, `features/storm/st-0003-availability-rules.feature`, `ST-0003`, `AC-0008`, `GR-008`, `SC-0003-002`, `TS-0002`, `TS-0014`

## 1. Overview / Цель
Добавить executable BDD layer для сценария `SC-0003-002`: `UnlockedDateTime` устанавливается, когда задача становится доступной, и очищается, когда задача становится недоступной. Сценарий уже связан с automated evidence, но пока не имеет repo-local step definitions.

Outcome contract:
- Success means: `SC-0003-002` получает новый automated executable BDD test, step definitions, passing targeted evidence, обновленные STORM artifacts и сохраненные existing links `TS-0002`/`TS-0014`.
- Итоговый артефакт / output: test-only executable spec + обновленные `storm.json` и reports.
- Stop rules: остановиться, если для прохождения нужны изменения product behavior, persisted schema, `.feature` wording, UI layout/selectors или existing test annotations.

## 2. Текущее состояние (AS-IS)
- `SC-0003-002` находится в `features/storm/st-0003-availability-rules.feature`, связан с `AC-0008`, `GR-008`, `TS-0002`, `TS-0014`, status = `automated`, `step_definitions = []`.
- `AC-0008` имеет coverage level `critical` за счет existing tests, но Gherkin steps не исполняются.
- `TaskAvailabilityCalculationTests` уже содержит доменные проверки установки и очистки `UnlockedDateTime` при изменении доступности.
- `TaskMigratorTests` и `JsonRepairingReaderTests` сохраняются как existing migration/storage evidence, но эта итерация не меняет миграции.
- `SC-0003-001` уже закрыт через `TS-0042` и `SD-0063..SD-0066`; `SC-0003-003` остается следующим gap для `ST-0003`.

## 3. Проблема
Traceability для `AC-0008` обрывается на linked existing tests. В `/storm:cover` нет исполняемой связи `Scenario -> Test -> Step Definition -> Code` для `SC-0003-002`, поэтому `ST-0003` остается частично covered на executable BDD layer.

## 4. Цели дизайна
- Разделение ответственности: product wording остается в `.feature`/`storm.json`, executable bridge живет в `src/Unlimotion.Test/StormBdd`.
- Повторное использование: использовать existing `StormFeatureParser`, `StormScenarioRunner`, `InMemoryStorage` и `TaskTreeManager`.
- Тестируемость: новый BDD test должен падать при изменении текста scenario steps или при регрессе установки/очистки `UnlockedDateTime`.
- Консистентность: продолжить ID-последовательность `TS-0043`, `SD-0067..SD-0070`.
- Обратная совместимость: не менять production API, persisted model, UI selectors, layout или behavior.

## 5. Non-Goals
- Не менять алгоритм availability.
- Не менять тексты `.feature`, acceptance criteria или test annotations.
- Не покрывать `SC-0003-003` в этой итерации.
- Не расширять scope на UI-очередь Unlocked, плановые даты или lifecycle rollback beyond `AC-0008`.
- Не запускать `/storm:full-cycle` и не пересоздавать product artifacts.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/StormBdd/TaskAvailabilityUnlockedTimeStepDefinitions.cs` -> step definitions `SD-0067..SD-0070` для `SC-0003-002`.
- `src/Unlimotion.Test/StormTaskAvailabilityUnlockedTimeExecutableSpecTests.cs` -> парсинг `SC-0003-002`, проверка tags и запуск steps.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> test-only context/result поля для передачи evidence между steps.
- `docs/product/storm.json` -> связи `SC-0003-002 -> TS-0043 -> SD-0067..SD-0070`, metrics и validation evidence.
- `docs/product/reports/*` -> обновить `/storm:cover`, `/storm:bdd-sync`, `/storm:bdd-lint`.

### 6.2 Детальный дизайн
- Step `Дано`: фиксирует наличие актуального набора задач для story context.
- Step `И`: подтверждает, что scenario относится к `ST-0003`.
- Step `Когда`: выполняет test-only проверки через `TaskTreeManager`:
  - blocked/not-available task без direct blockers пересчитывается в available, получает `UnlockedDateTime` и сохраняется;
  - previously available task с `UnlockedDateTime` и incomplete blocker пересчитывается в unavailable, теряет `UnlockedDateTime` и сохраняется.
- Step `Тогда`: подтверждает оба observable outcomes: дата установлена при появлении доступности и очищена при потере доступности.
- Visual planning artifact: не применяется, UI не меняется.
- UI test video evidence: не применяется, изменение не UI-facing и не меняет пользовательский flow. Проверка ограничена domain BDD, linked domain tests и full suite.
- Границы поведения: добавляется executable test layer и artifact sync; production behavior меняется только если targeted test выявит реальный дефект, тогда нужен отдельный bug/stability SPEC.

## 7. Бизнес-правила / Алгоритмы
- Когда задача становится доступной после пересчета availability, `UnlockedDateTime` устанавливается.
- Когда задача становится недоступной после пересчета availability, `UnlockedDateTime` очищается.
- Изменение должно сохраняться в storage через текущий `TaskTreeManager` flow.
- Existing migration/storage tests остаются source evidence для совместимости persisted данных.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario("features/storm/st-0003-availability-rules.feature", "SC-0003-002")`.
- `StormScenarioRunner` сопоставляет четыре Gherkin steps с `TaskAvailabilityUnlockedTimeStepDefinitions`.
- Проверяемые contracts: `TaskTreeManager.CalculateAndUpdateAvailability`, `InMemoryStorage.Load/Save`, `TaskItem.IsCanBeCompleted`, `TaskItem.UnlockedDateTime`, `TaskItem.BlockedByTasks`.

## 9. Изменения модели данных / состояния
- Production data/state: не меняется.
- Test-only state: in-memory task graphs.
- Test-only context: добавить result `TaskAvailabilityUnlockedTimeScenarioResult`.
- STORM artifact: добавить `TS-0043`, `SD-0067..SD-0070`, обновить `SC-0003-002`, `GR-008`, `ST-0003`, metrics/reports.

## 10. Миграция / Rollout / Rollback
- Production migration не требуется.
- Rollout: обычный test/artifact commit.
- Rollback: удалить новый executable spec/step definitions и откатить links/metrics `SC-0003-002`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `SC-0003-002` исполняется через repo-local step definitions.
  - Новый тест подтверждает tags `@scenario:SC-0003-002`, `@story:ST-0003`, `@test:TS-0002`, `@test:TS-0014`.
  - Новый test-only contract проверяет установку `UnlockedDateTime` при появлении доступности.
  - Новый test-only contract проверяет очистку `UnlockedDateTime` при потере доступности.
  - STORM validator проходит без errors.
- Команды проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal /nr:false`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskAvailabilityUnlockedTimeExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TaskAvailabilityCalculationTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - full `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed`
- Stop rules: если targeted evidence требует production behavior, UI change или миграцию, остановиться и оформить отдельную SPEC.

## 12. Риски и edge cases
- Риск: BDD step проверит только установку или только очистку. Смягчение: один scenario result должен содержать оба outcome.
- Риск: тест станет чувствительным к точному системному времени. Смягчение: проверять непустую дату и что она не раньше момента перед пересчетом, без equality по миллисекундам.
- Риск: linked migration evidence `TS-0014` будет ошибочно заявлен как новый targeted gate. Смягчение: сохранить его как existing evidence; targeted gate для этой итерации - BDD + `TaskAvailabilityCalculationTests`.
- Риск: full suite выявит unrelated order-dependent blocker. Смягчение: изолировать failed test и делать controlled retry; отдельная SPEC только при реальном blocker.

## 13. План выполнения
1. Создать SPEC и выполнить post-SPEC review.
2. Добавить test-only BDD result/context/step definitions и executable spec.
3. Обновить `storm.json` и reports через `/storm:bdd-sync`/`/storm:bdd-lint` по текущей структуре.
4. Запустить targeted tests, STORM validator, diff checks и full suite.
5. Выполнить post-EXEC review, исправить findings и закоммитить результат.

## 14. Открытые вопросы
Нет блокирующих.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`
- Выполненные требования профиля: сохраняется chain `Story -> AC -> Rule -> Scenario -> Test -> Step Definition -> Code`; Gherkin не заменяет AC; `/storm:bdd-implement` идет через QUEST; product artifacts на русском; используется TUnit `--treenode-filter`; UI profile не активируется, потому что UI behavior не меняется.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/StormBdd/TaskAvailabilityUnlockedTimeStepDefinitions.cs` | Новый test-only step definition набор | Исполнить `SC-0003-002` |
| `src/Unlimotion.Test/StormTaskAvailabilityUnlockedTimeExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result поля | Передать evidence между steps |
| `docs/product/storm.json` | Добавить `TS-0043`, `SD-0067..SD-0070`, links/metrics | `/storm:bdd-sync` |
| `docs/product/reports/coverage.md` | Обновить behavior coverage | `/storm:cover` report |
| `docs/product/reports/bdd-sync.md` | Обновить sync report | `/storm:bdd-sync` |
| `docs/product/reports/bdd-lint.md` | Обновить lint report | `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0003-002` | `automated`, linked tests, без step definitions | `passing`, linked tests + executable BDD test + step definitions |
| Behavior coverage | `17/45` scenarios with step definitions | `18/45` scenarios with step definitions |
| `ST-0003` executable coverage | `1/3` scenarios | `2/3` scenarios |
| Product behavior | Existing availability logic | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: изменить existing `TaskAvailabilityCalculationTests` и добавить annotations. Плюсы: меньше новых файлов. Минусы: нарушает ограничение на existing annotations и хуже audit trail. Отклонено.
- Вариант: покрыть сразу `SC-0003-002` и `SC-0003-003`. Плюсы: быстрее закрыть story. Минусы: смешивает `UnlockedDateTime` и lifecycle rollback. Отклонено.
- Выбранный вариант: один scenario-first executable BDD slice для `AC-0008`.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, state/rollback и business rules описаны |
| C. Безопасность изменений | 11-13 | PASS | Product behavior/schema/feature wording не меняются; rollback локальный |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria и команды проверки конкретны |
| E. Готовность к автономной реализации | 17-19 | PASS | План малый, блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | STORM/BDD chain, QUEST gate и TUnit workflow соблюдены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один UnlockedDateTime slice |
| 2. Понимание текущего состояния | 5 | Указаны scenario, AC, rule, tests and manager contracts |
| 3. Конкретность целевого дизайна | 5 | Перечислены файлы, steps, checks and artifact sync |
| 4. Безопасность (миграция, откат) | 5 | Нет production migration/schema/UX changes; rollback локальный |
| 5. Тестируемость | 5 | Targeted BDD/domain, validator and full suite |
| 6. Готовность к автономной реализации | 5 | Нет открытых блокеров |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-07-10-storm-sc0003-unlocked-time-bdd.md`, central stack, local `AGENTS.override.md`, `SC-0003-002`, `AC-0008`, `GR-008`, `TaskAvailabilityCalculationTests`, `TaskTreeManager.CalculateAndUpdateAvailability`, feature file
- Decision: можно выполнять; пользователь подтвердил SPEC фразой "спеку подтверждаю"
- Review passes:
  - Scope/Evidence pass: сверены `storm.json`, feature file, existing availability tests and manager code.
  - Contract pass: spec не требует production behavior changes, не меняет AC, feature wording или annotations.
  - Adversarial risk pass: установка и очистка `UnlockedDateTime` проверяются раздельно, без точного equality по системному времени.
  - Re-review after fixes / Fix and re-review: исправления не потребовались.
  - Stop decision: PASS.
- Evidence inspected: `features/storm/st-0003-availability-rules.feature`, `TaskAvailabilityCalculationTests`, `TaskTreeManager.CalculateAndUpdateAvailability`, current STORM reports.
- No-findings justification: scope малый, owner-doc requirements покрыты, open questions отсутствуют.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | `TS-0014` является preserved migration/storage evidence, а не новым targeted gate | Явно отделить targeted validation от preserved existing links | accepted-risk |

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved SPEC, `git status --short`, BDD step/test files, `docs/product/storm.json`, `docs/product/reports/*`, targeted test output, STORM validator output, full-suite logs.
- Decision: можно коммитить и продолжать `/storm:cover`.
- Review passes:
  - Scope/Evidence pass: `SC-0003-002` получил `TS-0043` and `SD-0067..SD-0070`; existing `TS-0002`/`TS-0014` preserved.
  - Contract pass: production code, `.feature` wording, project files, workflows and existing test annotations не менялись.
  - Adversarial risk pass: set/clear `UnlockedDateTime` проверены на сохраненном `InMemoryStorage` state; exact time equality не используется.
  - Re-review after fixes / Fix and re-review: unrelated full-suite UI blocker стабилизирован отдельной SPEC через existing Headless dispose helper; targeted UI passed 7/7 and full suite passed 574/574.
  - Stop decision: PASS.
- Evidence inspected:
  - `StormTaskAvailabilityUnlockedTimeExecutableSpecTests` прошло 1/1.
  - `TaskAvailabilityCalculationTests` прошло 26/26.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 8 warnings.
  - Initial full `Unlimotion.Test`: 573/574, unrelated `TreeSearch_ClearSearch_RestoresExpansionState(CompletedTree)` timeout; isolated rerun 7/7.
  - Controlled full retry: 573/574, same unrelated test failed in Avalonia.Headless `DisposeAsync` NRE.
  - After stability SPEC: targeted UI 7/7, full `Unlimotion.Test` 574/574.
- No-findings justification: BDD scope preserved, validation blocker handled in separate narrow test-only SPEC, remaining warnings are intentional duplicate shared steps.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full suite initially failed twice on unrelated order-sensitive UI test | Stabilized via separate SPEC and existing Headless dispose helper | fixed |

## Approval
Подтверждено пользователем: "спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0003-002` | 0.91 | Нет | Перейти к EXEC | Нет | Да, пользователь подтвердил SPEC | Второй scenario ST-0003 закрывает UnlockedDateTime rule без scope drift | `specs/2026-07-10-storm-sc0003-unlocked-time-bdd.md` |
| EXEC | `/storm:bdd-implement SC-0003-002` | 0.93 | Нет | Commit после финальных checks | Нет | Да, пользователь подтвердил SPEC | Scenario получил executable BDD links; targeted/domain/full gates passed after unrelated stability SPEC | `src/Unlimotion.Test/StormTaskAvailabilityUnlockedTimeExecutableSpecTests.cs`; `src/Unlimotion.Test/StormBdd/TaskAvailabilityUnlockedTimeStepDefinitions.cs`; `docs/product/storm.json`; `docs/product/reports/*` |
