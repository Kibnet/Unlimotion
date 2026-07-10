# STORM SC-0002-003: executable BDD для миграции истории статусов

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая ветка `storm-bootstrap`
- Ограничения: не менять product behavior; не менять acceptance criteria; не менять `.feature` wording; не менять existing test annotations; не удалять stories/tests/conflicts/dependencies; продуктовые артефакты вести на русском
- Связанные ссылки: `docs/product/storm.json`, `docs/product/reports/*`, `features/storm/st-0002-task-lifecycle.feature`, `ST-0002`, `AC-0006`, `GR-006`, `SC-0002-003`, `TS-0003`, `TS-0014`

## 1. Overview / Цель
Добавить executable BDD layer для сценария `SC-0002-003`: "История статусов и legacy-поля мигрируются без потери смысла." Сценарий уже связан с automated evidence, но не имеет repo-local step definitions.

Outcome contract:
- Success means: `SC-0002-003` имеет новый automated executable BDD test, step definitions, passing targeted evidence, обновлённые STORM artifacts и сохранённые existing links `TS-0003`/`TS-0014`.
- Итоговый артефакт / output: test-only executable spec + обновлённые `storm.json` и reports.
- Stop rules: остановиться, если для прохождения нужны изменения product behavior, persisted schema, `.feature` wording или existing test annotations.

## 2. Текущее состояние (AS-IS)
- `SC-0002-003` находится в `features/storm/st-0002-task-lifecycle.feature`, связан с `AC-0006`, `GR-006`, `TS-0003` и `TS-0014`, status = `automated`, `step_definitions = []`.
- `AC-0006` имеет coverage level `critical`: existing tests подтверждают миграцию, но Gherkin steps пока не исполняются.
- `TaskStatusMigrationTests` уже проверяет миграцию legacy `IsCompleted`, `CompletedDateTime`, `ArchiveDateTime` в `Status`/`StatusHistory` и удаление legacy-полей из JSON.
- `TaskMigratorTests` и `JsonRepairingReaderTests` остаются related storage/migration evidence в `TS-0014`, но для этого slice достаточно выполнить статусную миграцию `UnifiedTaskStorage.Init`.
- После предыдущей итерации `ST-0002` имеет 2/3 step-executable scenarios; `SC-0002-003` является следующим gap.

## 3. Проблема
Traceability для миграции статусов обрывается на linked existing tests. В `/storm:cover` нет исполняемой связи `Scenario -> Test -> Step Definition -> Code` для `SC-0002-003`, поэтому story `ST-0002` не закрыта полностью на executable BDD layer.

## 4. Цели дизайна
- Разделение ответственности: product wording остаётся в `.feature`/`storm.json`, executable bridge живёт в `src/Unlimotion.Test/StormBdd`.
- Повторное использование: использовать existing `StormFeatureParser`, `StormScenarioRunner`, `FileStorage`, `UnifiedTaskStorage` и `TaskTreeManager`.
- Тестируемость: новый BDD test должен падать при изменении текста scenario steps или при регрессе миграции legacy status fields.
- Консистентность: сохранить стиль `Storm*ExecutableSpecTests` и продолжить ID-последовательность `TS-0041`, `SD-0059..SD-0062`.
- Обратная совместимость: не менять production API, persisted model, migration algorithm, selectors, layout или behavior.

## 5. Non-Goals
- Не менять алгоритм миграции статусов.
- Не менять тексты `.feature`, acceptance criteria или test annotations.
- Не расширять scope на unrelated graph repair, JSON comma repair или startup projection.
- Не запускать `/storm:full-cycle` и не пересоздавать product artifacts.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/StormBdd/TaskStatusMigrationStepDefinitions.cs` -> step definitions `SD-0059..SD-0062` для `SC-0002-003`.
- `src/Unlimotion.Test/StormTaskStatusMigrationExecutableSpecTests.cs` -> парсинг `SC-0002-003`, проверка tags и запуск steps.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> test-only context/result поля для передачи migration evidence между steps.
- `docs/product/storm.json` -> связи `SC-0002-003 -> TS-0041 -> SD-0059..SD-0062`, metrics и validation evidence.
- `docs/product/reports/*` -> обновить `/storm:cover`, `/storm:bdd-sync`, `/storm:bdd-lint`.

### 6.2 Детальный дизайн
- Step `Дано`: фиксирует наличие актуального набора задач для story context.
- Step `И`: подтверждает, что scenario относится к `ST-0002`.
- Step `Когда`: создаёт временное task storage с legacy задачами:
  - active legacy task с `IsCompleted=false` мигрируется в `NotReady`, получает единственную историю `NotReady`, legacy fields удаляются;
  - completed legacy task с `IsCompleted=true` и `CompletedDateTime` мигрируется в `Completed`, получает историю `NotReady -> Completed`, дата завершения сохраняется;
  - archived legacy task с `IsCompleted=null` и `ArchiveDateTime` мигрируется в `Archived`, получает историю `NotReady -> Archived`, дата архива сохраняется;
  - status task без `StatusHistory` получает backfill `NotReady -> Prepared`.
- Step `Тогда`: подтверждает, что смысл legacy-полей сохранён через `Status`/`StatusHistory`, legacy fields удалены из persisted JSON, `StatusModelMigrationWasApplied = true`.
- Visual planning artifact: `Не применимо`; UI/visual flow не меняется.
- UI test video evidence: не применимо; slice не меняет UI behavior.
- Границы поведения: добавляется executable test layer и artifact sync; production behavior меняется только если targeted test выявит реальный дефект, тогда нужен отдельный stability/bug SPEC.

## 7. Бизнес-правила / Алгоритмы
- `IsCompleted=false` мигрируется в `Status=NotReady`.
- `IsCompleted=true` мигрируется в `Status=Completed`, а `CompletedDateTime` становится датой записи `Completed` в `StatusHistory`.
- `IsCompleted=null` мигрируется в `Status=Archived`, а `ArchiveDateTime` становится датой записи `Archived` в `StatusHistory`.
- Задача со `Status`, но без `StatusHistory`, получает историю из `CreatedDateTime` и `UpdatedDateTime`.
- Legacy поля `IsCompleted`, `CompletedDateTime`, `ArchiveDateTime` удаляются из persisted JSON после миграции.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario("features/storm/st-0002-task-lifecycle.feature", "SC-0002-003")`.
- `StormScenarioRunner` сопоставляет четыре Gherkin steps с `TaskStatusMigrationStepDefinitions`.
- Проверяемые contracts: `UnifiedTaskStorage.Init`, `FileStorage.Load`, persisted JSON file contents, `TaskItem.Status`, `TaskItem.StatusHistory`, `TaskItem.CompletedDateTime`, `TaskItem.ArchiveDateTime`.

## 9. Изменения модели данных / состояния
- Production data/state: не меняется.
- Test-only state: временные task files в `%TEMP%`, удаляются best-effort после сценария.
- Test-only context: добавить result `TaskStatusMigrationScenarioResult`.
- STORM artifact: добавить `TS-0041`, `SD-0059..SD-0062`, обновить `SC-0002-003`, `AC-0006`, `GR-006`, `ST-0002`, metrics/reports.

## 10. Миграция / Rollout / Rollback
- Production migration не требуется.
- Rollout: обычный test/artifact commit.
- Rollback: удалить новый executable spec/step definitions и откатить links/metrics `SC-0002-003`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `SC-0002-003` исполняется через repo-local step definitions.
  - Новый тест подтверждает tags `@scenario:SC-0002-003`, `@story:ST-0002`, `@test:TS-0003`, `@test:TS-0014`.
  - Новый test-only contract проверяет active/completed/archived/status-without-history migration cases.
  - Existing migration evidence проходит.
  - STORM validator проходит без errors.
- Команды проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal /nr:false`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskStatusMigrationExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TaskStatusMigrationTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - full `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed`
- Stop rules: если targeted evidence требует production behavior change, остановиться и оформить отдельный bug/stability SPEC.

## 12. Риски и edge cases
- Риск: BDD step продублирует слишком большую часть `TaskStatusMigrationTests`. Смягчение: покрыть только product-critical mapping cases из `AC-0006`.
- Риск: временные task files могут остаться после failed test. Смягчение: best-effort cleanup в `finally`.
- Риск: validator warnings увеличатся из-за intentional shared step text. Смягчение: validator должен оставаться без errors, warnings фиксируются в reports.
- Риск: full suite выявит unrelated order-dependent blocker. Смягчение: создать отдельную SPEC только если blocker мешает validation gate.

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
- Выполненные требования профиля: сохраняется chain `Story -> AC -> Rule -> Scenario -> Test -> Step Definition -> Code`; Gherkin не заменяет AC; `/storm:bdd-implement` идёт через QUEST; product artifacts на русском; UI automation requirement не применяется, потому что UI behavior не меняется.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/StormBdd/TaskStatusMigrationStepDefinitions.cs` | Новый test-only step definition набор | Исполнить `SC-0002-003` |
| `src/Unlimotion.Test/StormTaskStatusMigrationExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result поля | Передать evidence между steps |
| `docs/product/storm.json` | Добавить `TS-0041`, `SD-0059..SD-0062`, links/metrics | `/storm:bdd-sync` |
| `docs/product/reports/coverage.md` | Обновить behavior coverage | `/storm:cover` report |
| `docs/product/reports/bdd-sync.md` | Обновить sync report | `/storm:bdd-sync` |
| `docs/product/reports/bdd-lint.md` | Обновить lint report | `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0002-003` | `automated`, linked tests, без step definitions | `passing`, linked tests + executable BDD test + step definitions |
| Behavior coverage | `15/45` scenarios with step definitions | `16/45` scenarios with step definitions |
| `ST-0002` executable coverage | `2/3` scenarios | `3/3` scenarios |
| Product behavior | Existing migration logic | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: изменить existing `TaskStatusMigrationTests` и добавить annotations. Плюсы: меньше новых тестов. Минусы: нарушает ограничение на existing annotations и хуже audit trail. Отклонено.
- Вариант: покрыть весь `TS-0014` включая JSON repair и graph migration. Плюсы: шире storage evidence. Минусы: размывает `AC-0006`; это отдельные stories/scenarios. Отклонено.
- Выбранный вариант: один scenario-first executable BDD slice, который закрывает последний scenario `ST-0002`.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, state/rollback и business rules описаны |
| C. Безопасность изменений | 11-13 | PASS | Product behavior/schema/feature wording не меняются; rollback локальный |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria и команды проверки конкретны |
| E. Готовность к автономной реализации | 17-19 | PASS | План малый, блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | STORM/BDD chain, QUEST gate и product-language rule соблюдены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один migration regression slice |
| 2. Понимание текущего состояния | 5 | Указаны scenario, AC, rule, tests and migration contracts |
| 3. Конкретность целевого дизайна | 5 | Перечислены файлы, steps, checks and artifact sync |
| 4. Безопасность (миграция, откат) | 5 | Нет production migration/schema/UX changes; rollback локальный |
| 5. Тестируемость | 5 | Targeted BDD/migration, validator and full suite |
| 6. Готовность к автономной реализации | 5 | Нет открытых блокеров |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-29-storm-sc0002-status-migration-bdd.md`, central stack, local `AGENTS.override.md`, `SC-0002-003`, `AC-0006`, `GR-006`, `TaskStatusMigrationTests`, `UnifiedTaskStorage.TryMigrateTaskStatusJson`, feature file
- Decision: можно выполнять; active goal задаёт автоматическое подтверждение SPEC
- Review passes:
  - Scope/Evidence pass: сверены `storm.json`, feature file, existing migration tests and storage migration code.
  - Contract pass: spec не требует production behavior changes, не меняет AC, feature wording или annotations.
  - Adversarial risk pass: risk of over-broad storage coverage reduced by focusing only on status migration semantics.
  - Re-review after fixes / Fix and re-review: исправления не потребовались.
  - Stop decision: PASS.
- Evidence inspected: `features/storm/st-0002-task-lifecycle.feature`, `TaskStatusMigrationTests`, `TaskMigratorTests`, `JsonRepairingReaderTests`, `UnifiedTaskStorage.TryMigrateTaskStatusJson`.
- Depth checklist:
  - Scope drift / unrelated changes: planned files ограничены BDD test layer + STORM artifacts.
  - Acceptance criteria: прямо связан с `AC-0006`.
  - Validation evidence: targeted migration, validator, diff and full suite commands указаны.
  - Unsupported claims: runtime/release claims отсутствуют.
  - Regression / edge case: active/completed/archived/backfill cases separated.
  - Comments/docs/changelog: changelog не требуется.
  - Hidden contract change: production behavior не меняется; если понадобится, stop rule требует отдельную SPEC.
  - Manual-review challenge: reviewer проверил бы, что BDD step действительно читает migrated persisted JSON, а не только in-memory status.
- No-findings justification: scope малый, owner-doc requirements покрыты, open questions отсутствуют.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | scope | `TS-0014` шире, чем status migration | Ограничить executable slice только `AC-0006` и сохранить broader links как existing evidence | accepted-risk |

## Approval
Автоматически подтверждено активной целью пользователя: "я автоматически спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0002-003` | 0.91 | Нет | Перейти к EXEC | Нет | Да, active goal задаёт auto approval | Последний scenario `ST-0002` закрывает migration regression без scope drift | `specs/2026-06-29-storm-sc0002-status-migration-bdd.md` |
| EXEC | /storm:bdd-implement SC-0002-003 | 0.93 | Нет | Commit после финальных checks | Нет | Да, active goal задаёт auto approval | Scenario получил executable BDD links; targeted/migration/full gates passed | src/Unlimotion.Test/StormTaskStatusMigrationExecutableSpecTests.cs; src/Unlimotion.Test/StormBdd/TaskStatusMigrationStepDefinitions.cs; docs/product/storm.json; docs/product/reports/* |

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, relevant BDD step/test files, `docs/product/storm.json`, `docs/product/reports/*`, targeted test output, STORM validator output, full-suite log `C:\tmp\unlimotion-full-suite-sc0002-status-migration-bdd.log`
- Decision: можно завершать и коммитить
- Review passes:
  - Scope/Evidence pass: проверены planned files; production code, `.feature`, project files, workflows and existing test annotations не менялись.
  - Contract pass: `SC-0002-003` получил `TS-0041` and `SD-0059..SD-0062`; `TS-0003`/`TS-0014` сохранены; `AC-0006` поднят до full coverage на executable layer.
  - Adversarial risk pass: BDD step проверяет actual `UnifiedTaskStorage.Init`, persisted JSON cleanup, status history ordering and legacy date projections, а не только tags.
  - Re-review after fixes / Fix and re-review: после artifact sync обновлены pending full-suite строки на фактический 572/572 evidence.
  - Stop decision: PASS.
- Evidence inspected:
  - `StormTaskStatusMigrationExecutableSpecTests` прошло 1/1.
  - `TaskStatusMigrationTests` прошло 5/5.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 6 warnings.
  - Full `Unlimotion.Test` прошёл 572/572 вне managed sandbox.
- Depth checklist:
  - Scope drift / unrelated changes: изменений вне planned BDD/spec/artifact files нет.
  - Acceptance criteria: `AC-0006` закрыт через `SC-0002-003 -> TS-0041 -> SD-0059..SD-0062`.
  - Validation evidence: targeted, migration, validator, full suite and diff checks covered.
  - Unsupported claims: runtime/release claims отсутствуют.
  - Regression / edge case: active/completed/archived/backfill cases checked separately.
  - Comments/docs/changelog: новых code comments нет; changelog не требуется.
  - Hidden contract change: production behavior не менялся.
  - Manual-review challenge: reviewer мог бы спросить, почему весь `TS-0014` не исполняется в BDD step; ответ: slice intentionally scoped to `AC-0006`, broader storage/JSON evidence сохранено как existing links.
- No-findings justification: diff соответствует spec, проверки покрывают BDD/migration/full gates, validator без errors.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | scope | `TS-0014` шире, чем статусная миграция | Broader links сохранены как existing evidence; executable slice ограничен `AC-0006` | accepted-risk |

- Fixed before final report: pending full-suite evidence заменён на 572/572 в `storm.json` и reports
- Checks rerun: STORM validator, `git diff --check`, trailing whitespace scan
- Validation evidence: listed above
- Unrelated changes: нет
- Needs human: нет
- Residual risks / follow-ups: следующий `/storm:cover` slice должен перейти к оставшимся scenarios без step definitions, текущий ближайший candidate `SC-0003-001`
