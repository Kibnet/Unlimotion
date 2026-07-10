# STORM SC-0003-001: executable BDD для правил недоступности задач

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая ветка `storm-bootstrap`
- Ограничения: не менять product behavior; не менять acceptance criteria; не менять `.feature` wording; не менять existing test annotations; не удалять stories/tests/conflicts/dependencies; продуктовые артефакты вести на русском
- Связанные ссылки: `docs/product/storm.json`, `docs/product/reports/*`, `features/storm/st-0003-availability-rules.feature`, `ST-0003`, `AC-0007`, `GR-007`, `SC-0003-001`, `TS-0002`, `TS-0003`, `TS-0005`

## 1. Overview / Цель
Добавить executable BDD layer для сценария `SC-0003-001`: задача считается недоступной, если у неё есть незавершённые дочерние задачи, блокирующие задачи или блокировки в родительской цепочке. Сценарий уже связан с automated evidence, но не имеет repo-local step definitions.

Outcome contract:
- Success means: `SC-0003-001` имеет новый automated executable BDD test, step definitions, passing targeted evidence, обновлённые STORM artifacts и сохранённые existing links `TS-0002`/`TS-0003`/`TS-0005`.
- Итоговый артефакт / output: test-only executable spec + обновлённые `storm.json` и reports.
- Stop rules: остановиться, если для прохождения нужны изменения product behavior, persisted schema, `.feature` wording, UI layout/selectors или existing test annotations.

## 2. Текущее состояние (AS-IS)
- `SC-0003-001` находится в `features/storm/st-0003-availability-rules.feature`, связан с `AC-0007`, `GR-007`, `TS-0002`, `TS-0003`, `TS-0005`, status = `automated`, `step_definitions = []`.
- `AC-0007` уже имеет coverage level `full` за счёт existing tests, но Gherkin steps не исполняются.
- `TaskTreeManager.CalculateAvailabilityForTask` вычисляет `IsCanBeCompleted` через завершённость дочерних задач и incomplete blockers в задаче или parent chain.
- `TaskAvailabilityCalculationTests` уже проверяет incomplete child, incomplete blocker, inherited ancestor blocker and multi-parent blocker cases.
- `MainControlAvailabilityUiTests` подтверждает UI-facing проявления недоступности: dimmed title и disabled Completed option для descendant при ancestor blocker.

## 3. Проблема
Traceability для ключевого availability rule обрывается на linked existing tests. В `/storm:cover` нет исполняемой связи `Scenario -> Test -> Step Definition -> Code` для `SC-0003-001`, поэтому `ST-0003` остаётся без executable BDD coverage.

## 4. Цели дизайна
- Разделение ответственности: product wording остаётся в `.feature`/`storm.json`, executable bridge живёт в `src/Unlimotion.Test/StormBdd`.
- Повторное использование: использовать existing `StormFeatureParser`, `StormScenarioRunner`, `InMemoryStorage` и `TaskTreeManager`.
- Тестируемость: новый BDD test должен падать при изменении текста scenario steps или при регрессе трёх причин недоступности.
- Консистентность: продолжить ID-последовательность `TS-0042`, `SD-0063..SD-0066`.
- Обратная совместимость: не менять production API, persisted model, UI selectors, layout или behavior.

## 5. Non-Goals
- Не менять алгоритм availability.
- Не менять тексты `.feature`, acceptance criteria или test annotations.
- Не покрывать `SC-0003-002` и `SC-0003-003` в этой итерации.
- Не расширять scope на planned dates или Unlocked queue beyond `AC-0007`.
- Не запускать `/storm:full-cycle` и не пересоздавать product artifacts.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/StormBdd/TaskAvailabilityBlockersStepDefinitions.cs` -> step definitions `SD-0063..SD-0066` для `SC-0003-001`.
- `src/Unlimotion.Test/StormTaskAvailabilityBlockersExecutableSpecTests.cs` -> парсинг `SC-0003-001`, проверка tags и запуск steps.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> test-only context/result поля для передачи availability evidence между steps.
- `docs/product/storm.json` -> связи `SC-0003-001 -> TS-0042 -> SD-0063..SD-0066`, metrics и validation evidence.
- `docs/product/reports/*` -> обновить `/storm:cover`, `/storm:bdd-sync`, `/storm:bdd-lint`.

### 6.2 Детальный дизайн
- Step `Дано`: фиксирует наличие актуального набора задач для story context.
- Step `И`: подтверждает, что scenario относится к `ST-0003`.
- Step `Когда`: выполняет test-only проверки через `TaskTreeManager`:
  - parent task с незавершённым child становится unavailable: `IsCanBeCompleted=false`, `UnlockedDateTime=null`;
  - task с direct incomplete blocker становится unavailable;
  - descendant task становится unavailable, если ancestor имеет incomplete blocker, без прямого `BlockedByTasks` на descendant.
- Step `Тогда`: подтверждает, что все три причины дают unavailable outcome и не подменяются unrelated relation state.
- Visual planning artifact: не применяется, UI не меняется.
- UI test video evidence: fallback; текущий Avalonia.Headless/TUnit workflow не сохраняет видео. Next-best evidence = targeted `MainControlAvailabilityUiTests` output and full `Unlimotion.Test` log.
- Границы поведения: добавляется executable test layer и artifact sync; production behavior меняется только если targeted test выявит реальный дефект, тогда нужен отдельный bug/stability SPEC.

## 7. Бизнес-правила / Алгоритмы
- Задача недоступна, если хотя бы одна contained task incomplete для availability.
- Задача недоступна, если хотя бы один direct blocker incomplete.
- Задача недоступна, если incomplete blocker есть у неё или у любого ancestor в parent chain.
- При недоступности `UnlockedDateTime` очищается.
- Inherited blocker не должен записываться как direct blocker на descendant.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario("features/storm/st-0003-availability-rules.feature", "SC-0003-001")`.
- `StormScenarioRunner` сопоставляет четыре Gherkin steps с `TaskAvailabilityBlockersStepDefinitions`.
- Проверяемые contracts: `TaskTreeManager.CalculateAndUpdateAvailability`, `InMemoryStorage.Load/Save`, `TaskItem.IsCanBeCompleted`, `TaskItem.UnlockedDateTime`, `TaskItem.BlockedByTasks`.

## 9. Изменения модели данных / состояния
- Production data/state: не меняется.
- Test-only state: in-memory task graphs.
- Test-only context: добавить result `TaskAvailabilityBlockersScenarioResult`.
- STORM artifact: добавить `TS-0042`, `SD-0063..SD-0066`, обновить `SC-0003-001`, `GR-007`, `ST-0003`, metrics/reports.

## 10. Миграция / Rollout / Rollback
- Production migration не требуется.
- Rollout: обычный test/artifact commit.
- Rollback: удалить новый executable spec/step definitions и откатить links/metrics `SC-0003-001`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `SC-0003-001` исполняется через repo-local step definitions.
  - Новый тест подтверждает tags `@scenario:SC-0003-001`, `@story:ST-0003`, `@test:TS-0002`, `@test:TS-0003`, `@test:TS-0005`.
  - Новый test-only contract проверяет incomplete child, direct incomplete blocker and inherited ancestor blocker.
  - Existing availability UI evidence проходит targeted run.
  - STORM validator проходит без errors.
- Команды проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal /nr:false`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskAvailabilityBlockersExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TaskAvailabilityCalculationTests/TaskWithIncompleteChild_ShouldNotBeAvailable|/*/*/TaskAvailabilityCalculationTests/TaskWithIncompleteBlocker_ShouldNotBeAvailable|/*/*/TaskAvailabilityCalculationTests/Grandchild_ShouldInheritIncompleteBlockerFromAncestor" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlAvailabilityUiTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - full `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed`
- Stop rules: если targeted evidence требует production behavior or UI change, остановиться и оформить отдельный bug/stability SPEC.

## 12. Риски и edge cases
- Риск: BDD step проверит слишком узкий happy path. Смягчение: покрыть три независимых blockers from AC wording.
- Риск: inherited blocker может быть спутан с direct relation. Смягчение: явно проверить, что descendant `BlockedByTasks` пустой.
- Риск: UI video evidence отсутствует. Смягчение: использовать targeted Avalonia.Headless tests как next-best evidence.
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
- Профиль: `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля: сохраняется chain `Story -> AC -> Rule -> Scenario -> Test -> Step Definition -> Code`; Gherkin не заменяет AC; `/storm:bdd-implement` идёт через QUEST; product artifacts на русском; UI-facing behavior дополнительно проверяется existing Avalonia.Headless tests.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/StormBdd/TaskAvailabilityBlockersStepDefinitions.cs` | Новый test-only step definition набор | Исполнить `SC-0003-001` |
| `src/Unlimotion.Test/StormTaskAvailabilityBlockersExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result поля | Передать evidence между steps |
| `docs/product/storm.json` | Добавить `TS-0042`, `SD-0063..SD-0066`, links/metrics | `/storm:bdd-sync` |
| `docs/product/reports/coverage.md` | Обновить behavior coverage | `/storm:cover` report |
| `docs/product/reports/bdd-sync.md` | Обновить sync report | `/storm:bdd-sync` |
| `docs/product/reports/bdd-lint.md` | Обновить lint report | `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0003-001` | `automated`, linked tests, без step definitions | `passing`, linked tests + executable BDD test + step definitions |
| Behavior coverage | `16/45` scenarios with step definitions | `17/45` scenarios with step definitions |
| `ST-0003` executable coverage | `0/3` scenarios | `1/3` scenarios |
| Product behavior | Existing availability logic | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: изменить existing `TaskAvailabilityCalculationTests` и добавить annotations. Плюсы: меньше новых файлов. Минусы: нарушает ограничение на existing annotations и хуже audit trail. Отклонено.
- Вариант: покрыть сразу все `ST-0003` scenarios. Плюсы: быстрее закрыть story. Минусы: смешивает availability blockers, UnlockedDateTime и InProgress rollback. Отклонено.
- Выбранный вариант: один scenario-first executable BDD slice для `AC-0007`.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, state/rollback и business rules описаны |
| C. Безопасность изменений | 11-13 | PASS | Product behavior/schema/feature wording не меняются; rollback локальный |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria и команды проверки конкретны |
| E. Готовность к автономной реализации | 17-19 | PASS | План малый, блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | STORM/BDD chain, QUEST gate и UI-test fallback соблюдены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один availability blockers slice |
| 2. Понимание текущего состояния | 5 | Указаны scenario, AC, rule, tests and manager contracts |
| 3. Конкретность целевого дизайна | 5 | Перечислены файлы, steps, checks and artifact sync |
| 4. Безопасность (миграция, откат) | 5 | Нет production migration/schema/UX changes; rollback локальный |
| 5. Тестируемость | 5 | Targeted BDD/domain/UI, validator and full suite |
| 6. Готовность к автономной реализации | 5 | Нет открытых блокеров |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-29-storm-sc0003-availability-blockers-bdd.md`, central stack, local `AGENTS.override.md`, `SC-0003-001`, `AC-0007`, `GR-007`, `TaskAvailabilityCalculationTests`, `MainControlAvailabilityUiTests`, `TaskTreeManager.CalculateAvailabilityForTask`, feature file
- Decision: можно выполнять; active goal задаёт автоматическое подтверждение SPEC
- Review passes:
  - Scope/Evidence pass: сверены `storm.json`, feature file, existing availability tests and manager code.
  - Contract pass: spec не требует production behavior changes, не меняет AC, feature wording или annotations.
  - Adversarial risk pass: three independent blocker reasons are explicitly covered; inherited blocker is checked as inherited rather than direct relation.
  - Re-review after fixes / Fix and re-review: исправления не потребовались.
  - Stop decision: PASS.
- Evidence inspected: `features/storm/st-0003-availability-rules.feature`, `TaskAvailabilityCalculationTests`, `MainControlAvailabilityUiTests`, `TaskTreeManager.CalculateAvailabilityForTask`.
- No-findings justification: scope малый, owner-doc requirements покрыты, open questions отсутствуют.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | UI video evidence отсутствует для Avalonia.Headless workflow | Использовать fallback: targeted headless UI test output + full suite log | accepted-risk |

## Approval
Автоматически подтверждено активной целью пользователя: "я автоматически спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0003-001` | 0.91 | Нет | Перейти к EXEC | Нет | Да, active goal задаёт auto approval | Первый scenario ST-0003 закрывает blockers rule без scope drift | `specs/2026-06-29-storm-sc0003-availability-blockers-bdd.md` |
| EXEC | /storm:bdd-implement SC-0003-001 | 0.93 | Нет | Commit после финальных checks | Нет | Да, active goal задаёт auto approval | Scenario получил executable BDD links; targeted/domain/UI/full retry gates passed | src/Unlimotion.Test/StormTaskAvailabilityBlockersExecutableSpecTests.cs; src/Unlimotion.Test/StormBdd/TaskAvailabilityBlockersStepDefinitions.cs; docs/product/storm.json; docs/product/reports/* |
### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, relevant BDD step/test files, `docs/product/storm.json`, `docs/product/reports/*`, targeted test output, STORM validator output, full-suite logs `C:\tmp\unlimotion-full-suite-sc0003-availability-blockers-bdd.log` and `C:\tmp\unlimotion-full-suite-sc0003-availability-blockers-bdd-retry.log`
- Decision: можно завершать и коммитить
- Review passes:
  - Scope/Evidence pass: проверены planned files; production code, `.feature`, project files, workflows and existing test annotations не менялись.
  - Contract pass: `SC-0003-001` получил `TS-0042` and `SD-0063..SD-0066`; `TS-0002`/`TS-0003`/`TS-0005` сохранены; `AC-0007` remains full coverage and now has executable BDD bridge.
  - Adversarial risk pass: BDD step проверяет incomplete child, direct blocker and inherited ancestor blocker separately; inherited blocker не записывается в descendant direct `BlockedByTasks`.
  - Re-review after fixes / Fix and re-review: initial full-suite Headless transient isolated; failed test passed targeted 1/1; controlled retry passed 573/573.
  - Stop decision: PASS.
- Evidence inspected:
  - `StormTaskAvailabilityBlockersExecutableSpecTests` прошло 1/1.
  - `TaskAvailabilityCalculationTests` прошло 26/26.
  - `MainControlAvailabilityUiTests` прошло 2/2.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 7 warnings.
  - Initial full `Unlimotion.Test` run: 572/573, unrelated `TreeCommandUi_ShiftDelete_RemovesSelectedLastUpdatedTreeItem` Headless dispose failure.
  - Isolated failed test rerun: passed 1/1.
  - Controlled full `Unlimotion.Test` retry: passed 573/573.
- Depth checklist:
  - Scope drift / unrelated changes: изменений вне planned BDD/spec/artifact files нет.
  - Acceptance criteria: `AC-0007` закрыт через `SC-0003-001 -> TS-0042 -> SD-0063..SD-0066`.
  - Validation evidence: targeted, UI, validator, full suite retry and diff checks covered.
  - Unsupported claims: runtime/release claims отсутствуют.
  - Regression / edge case: inherited blocker checked without direct child blocker relation.
  - Comments/docs/changelog: новых code comments нет; changelog не требуется.
  - Hidden contract change: production behavior не менялся.
  - Manual-review challenge: reviewer мог бы спросить, почему full suite had an initial failure; ответ: failing test was unrelated to changed files, passed targeted 1/1, and full-suite controlled retry passed 573/573.
- No-findings justification: diff соответствует spec, проверки покрывают BDD/domain/UI/full gates, validator без errors.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | UI video evidence отсутствует для Avalonia.Headless workflow | Использован fallback: targeted headless UI test output + full-suite log | accepted-risk |
| LOW | validation | Initial full-suite run caught unrelated Headless transient | Isolated failed test passed 1/1; controlled full-suite retry passed 573/573 | resolved |

- Fixed before final report: pending full-suite evidence заменён на retry 573/573 в `storm.json` и reports
- Checks rerun: STORM validator, `git diff --check`, trailing whitespace scan
- Validation evidence: listed above
- Unrelated changes: нет
- Needs human: нет
- Residual risks / follow-ups: `SC-0003-002` и `SC-0003-003` остаются следующими `/storm:cover` candidates для `ST-0003`
