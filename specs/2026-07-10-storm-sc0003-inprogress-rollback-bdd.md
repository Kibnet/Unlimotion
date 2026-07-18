# STORM SC-0003-003: executable BDD для rollback недоступного InProgress

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `storm-bootstrap`
- Ограничения: не менять product behavior; не менять acceptance criteria; не менять `.feature` wording; не менять existing test annotations; продуктовые артефакты вести на русском
- Связанные ссылки: `ST-0003`, `AC-0009`, `GR-009`, `SC-0003-003`, `TS-0002`, `TS-0003`, `TaskTreeManager.ApplyAutomaticInProgressRollbackIfNeeded`

## 1. Overview / Цель
Добавить executable BDD layer для `SC-0003-003`: если задача стала недоступной, недопустимое состояние `InProgress` корректируется обратно в допустимый статус.

Outcome contract:
- Success means: `SC-0003-003` получает новый executable BDD test `TS-0044`, step definitions `SD-0071..SD-0074`, passing targeted/full evidence, а `ST-0003` становится 3/3 step-executable.
- Итоговый артефакт / output: test-only executable spec + обновленные `storm.json` и reports.
- Stop rules: остановиться, если нужны изменения production behavior, persisted schema, `.feature` wording, UI или existing annotations.

## 2. Текущее состояние (AS-IS)
- `SC-0003-003` связан с `AC-0009`, `GR-009`, `TS-0002`, `TS-0003`, status = `automated`, `step_definitions = []`.
- `SC-0003-001` и `SC-0003-002` уже passing/step-executable.
- Existing tests подтверждают rollback недопустимого `InProgress`: `UpdateTask_InProgressTaskWithUnavailableFlag_RollsBackToPrepared`.
- Production code делает rollback в `TaskTreeManager.ApplyAutomaticInProgressRollbackIfNeeded` при `!IsCanBeCompleted` или future planned begin.

## 3. Проблема
`ST-0003` остается частично covered на executable BDD layer: для `AC-0009` нет исполняемой связи `Scenario -> Test -> Step Definition -> Code`.

## 4. Цели дизайна
- Закрыть только `SC-0003-003` и завершить `ST-0003` до 3/3 executable scenarios.
- Проверить реальный availability transition: `InProgress` задача теряет доступность из-за incomplete blocker.
- Проверить observable результат: `Status=Prepared`, latest history `Prepared`, author `System`, `IsCanBeCompleted=false`, `UnlockedDateTime=null`.
- Не менять product code и existing tests.

## 5. Non-Goals
- Не менять status transition policy.
- Не покрывать future planned begin rollback в этой итерации.
- Не менять UI status picker behavior.
- Не запускать `/storm:full-cycle`.

## 6. Предлагаемое решение (TO-BE)
- Добавить `TaskAvailabilityInProgressRollbackStepDefinitions` с `SD-0071..SD-0074`.
- Добавить `StormTaskAvailabilityInProgressRollbackExecutableSpecTests` с `TS-0044`.
- Расширить `StormScenarioContext` test-only result fields.
- Синхронизировать `storm.json`, `coverage.md`, `bdd-sync.md`, `bdd-lint.md`.

## 7. Бизнес-правила / Алгоритмы
- `InProgress` допустим только для startable task.
- Когда availability пересчет делает задачу unavailable, `InProgress` автоматически заменяется на `Prepared`.
- Системная коррекция фиксируется в `StatusHistory` с author `System`.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario(..., "SC-0003-003")`.
- `StormScenarioRunner` executes four feature steps.
- Domain contracts: `TaskTreeManager.CalculateAndUpdateAvailability`, `TaskItem.SetStatus`, `StatusHistory`, `InMemoryStorage.Load/Save`.

## 9. Изменения модели данных / состояния
Production state не меняется. Test-only fixture создает in-memory task graph with incomplete blocker.

## 10. Миграция / Rollout / Rollback
Migration не требуется. Rollback: удалить `TS-0044`, `SD-0071..SD-0074` и откатить artifact links/metrics.

## 11. Тестирование и критерии приёмки
- `SC-0003-003` исполняется через repo-local steps.
- Tags `@scenario:SC-0003-003`, `@story:ST-0003`, `@test:TS-0002`, `@test:TS-0003` проверены.
- Targeted BDD проходит 1/1.
- `TaskStatusTransitionTests` проходит как preserved linked evidence.
- `TaskAvailabilityCalculationTests` проходит как preserved linked evidence.
- STORM validator проходит 0 errors.
- Full `Unlimotion.Test` проходит.

## 12. Риски и edge cases
- Риск: test проверит direct unavailable flag, а не availability transition. Смягчение: fixture использует incomplete blocker and `CalculateAndUpdateAvailability`.
- Риск: time-sensitive history. Смягчение: проверять status/author, не exact timestamp.

## 13. План выполнения
1. Создать SPEC и post-SPEC review.
2. Добавить test-only context, step definitions, executable spec.
3. Обновить STORM artifacts/reports.
4. Запустить targeted/domain/full validation.
5. Post-EXEC review и commit.

## 14. Открытые вопросы
Нет блокирующих.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`
- Выполненные требования: QUEST gate, Scenario -> Test -> Step Definition -> Code, product artifacts на русском, TUnit `--treenode-filter`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/StormBdd/TaskAvailabilityInProgressRollbackStepDefinitions.cs` | Новый step definition набор | Исполнить `SC-0003-003` |
| `src/Unlimotion.Test/StormTaskAvailabilityInProgressRollbackExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result fields | Передать evidence между steps |
| `docs/product/storm.json`, `docs/product/reports/*` | Links/metrics/reports | `/storm:bdd-sync`, `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0003-003` | `automated`, no steps | `passing`, `TS-0044`, `SD-0071..SD-0074` |
| `ST-0003` executable coverage | 2/3 | 3/3 |
| Step-executable scenarios | 18/45 | 19/45 |
| Product behavior | Existing rollback logic | Без изменений |

## 18. Альтернативы и компромиссы
- Покрыть future planned begin вместе с unavailable blocker: отклонено, это смешивает AC-0009 with scheduling behavior.
- Менять existing tests/annotations: отклонено, audit trail хуже и нарушает constraint.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals есть |
| B. Качество дизайна | 6-10 | PASS | Файлы, contracts, rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Test-only, без product behavior changes |
| D. Проверяемость | 14-16 | PASS | Targeted/domain/full checks заданы |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет blockers |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST/TUnit соблюдены |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один scenario |
| 2. Понимание текущего состояния | 5 | Existing tests/code указаны |
| 3. Конкретность целевого дизайна | 5 | IDs/files/checks заданы |
| 4. Безопасность | 5 | No production/schema/UI changes |
| 5. Тестируемость | 5 | Targeted/domain/full |
| 6. Готовность | 5 | Нет open questions |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `SC-0003-003`, `AC-0009`, `GR-009`, `TaskStatusTransitionTests`, `TaskAvailabilityCalculationTests`, `TaskTreeManager.ApplyAutomaticInProgressRollbackIfNeeded`.
- Decision: можно выполнять; workflow пользователя подтверждает SPEC.
- Review passes: Scope/Evidence PASS; Contract PASS; Adversarial risk PASS; Stop decision PASS.
- No-findings justification: slice малый, проверяет transition through availability recalculation, а не прямую подмену status.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | scope | Future planned begin rollback не покрывается | Оставить вне scope, потому что scenario про потерю доступности | accepted-risk |

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: `SC-0003-003`, `AC-0009`, `GR-009`, `TS-0044`, `SD-0071..SD-0074`, `TaskTreeManager.ApplyAutomaticInProgressRollbackIfNeeded`.
- Implemented: добавлен test-only executable BDD bridge, обновлены `storm.json` и reports, production code / `.feature` wording / existing annotations не менялись.
- Validation:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal /nr:false` => прошло после approved network/cache escalation; existing warnings, errors 0.
  - `StormTaskAvailabilityInProgressRollbackExecutableSpecTests` => прошло 1/1.
  - `TaskStatusTransitionTests` => прошло 18/18.
  - `TaskAvailabilityCalculationTests` => прошло 26/26.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` => OK: 0 errors, 8 warnings по intentional shared steps.
  - Initial full `Unlimotion.Test` => 573/575, unrelated `Avalonia.Headless.DisposeAsync` NRE in two UI tests; both passed isolated 1/1.
  - Controlled full retry => passed 575/575 with `C:\tmp\unlimotion-full-suite-sc0003-inprogress-rollback-bdd-retry.log`.
- Decision: slice готов к commit; initial full-suite failure classified as unrelated headless teardown flake because isolated failed tests passed and controlled full retry passed.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Initial full run hit two unrelated Avalonia.Headless teardown NREs | Preserve evidence and rely on isolated pass + controlled full retry; do not change UI tests/code in this BDD slice | accepted-risk |

## Approval
Подтверждено текущим workflow пользователя: SPEC auto-approved for execution.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0003-003` | 0.91 | Нет | Перейти к EXEC | Нет | Да, workflow auto approval | Закрывает последний scenario ST-0003 без product-code changes | `specs/2026-07-10-storm-sc0003-inprogress-rollback-bdd.md` |
| EXEC | executable BDD slice | 0.93 | Нет | Commit и перейти к следующему `/storm:cover` candidate | Нет | Нет | Targeted/domain/full gates passed; ST-0003 закрыт 3/3 step-executable | `src/Unlimotion.Test/StormTaskAvailabilityInProgressRollbackExecutableSpecTests.cs`, `src/Unlimotion.Test/StormBdd/TaskAvailabilityInProgressRollbackStepDefinitions.cs`, `docs/product/storm.json`, `docs/product/reports/*` |
