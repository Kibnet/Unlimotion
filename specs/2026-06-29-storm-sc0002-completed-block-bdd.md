# STORM SC-0002-002: executable BDD для блокировки перехода в Completed

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая ветка `storm-bootstrap`
- Ограничения: не менять product behavior; не менять acceptance criteria; не менять `.feature` wording; не менять existing test annotations; не удалять stories/tests/conflicts/dependencies; продуктовые артефакты вести на русском
- Связанные ссылки: `docs/product/storm.json`, `docs/product/reports/*`, `features/storm/st-0002-task-lifecycle.feature`, `ST-0002`, `AC-0005`, `GR-005`, `SC-0002-002`, `TS-0003`, `TS-0005`

## 1. Overview / Цель
Добавить executable BDD layer для сценария `SC-0002-002`: "Переход в Completed блокируется, если задача недоступна или критерии завершения не выполнены." Сценарий уже связан с automated evidence, но не имеет repo-local step definitions.

Outcome contract:
- Success means: `SC-0002-002` имеет новый automated executable BDD test, step definitions, passing targeted evidence, обновлённые STORM artifacts и сохранённые existing links `TS-0003`/`TS-0005`.
- Итоговый артефакт / output: test-only executable spec + обновлённые `storm.json` и reports.
- Stop rules: остановиться, если для прохождения нужны изменения product behavior, `.feature` wording, existing test annotations, persisted schema или публичного UI contract.

## 2. Текущее состояние (AS-IS)
- `SC-0002-002` находится в `features/storm/st-0002-task-lifecycle.feature`, связан с `AC-0005`, `GR-005`, `TS-0003` и `TS-0005`, status = `automated`, `step_definitions = []`.
- `AC-0005` имеет coverage level `critical`, потому что behavior подтверждён existing tests, но не исполняется через BDD step layer.
- `TaskTreeManager.CanTransitionToStatus` блокирует `Completed`, если задача archived, если completion criteria не удовлетворены, если contained tasks не завершены или есть incomplete blocker в ancestors.
- `TaskItemViewModel.CanTransitionToStatus` выключает `Completed` option при невыполненных completion criteria и unavailable graph state; `TaskStatusPicker` показывает только available transitions.
- Existing UI evidence есть в `MainControlTaskStatusIconUiTests`, включая `TaskStatusPickerFlyout_EnablesCompletedOptionAfterCriterionIsSatisfied`.

## 3. Проблема
`SC-0002-002` участвует в `/storm:cover`, но его Gherkin steps не исполняются. Сейчас traceability для negative path обрывается на linked existing tests, а не проходит полный путь `Scenario -> Test -> Step Definition -> Code`.

## 4. Цели дизайна
- Разделение ответственности: product wording остаётся в `.feature`/`storm.json`, executable bridge живёт в `src/Unlimotion.Test/StormBdd`.
- Повторное использование: использовать existing `StormFeatureParser`, `StormScenarioRunner`, domain manager и ViewModel contracts.
- Тестируемость: новый BDD test должен падать при изменении текста scenario steps или при разрешении `Completed` для unavailable/unsatisfied task.
- Консистентность: сохранить стиль `Storm*ExecutableSpecTests` и ID-последовательность `TS-0040`, `SD-0055..SD-0058`.
- Обратная совместимость: не менять production API, persisted model, selectors, layout или behavior.

## 5. Non-Goals
- Не менять правила переходов статусов.
- Не менять тексты `.feature`, acceptance criteria или test annotations.
- Не покрывать `SC-0002-003` в этой итерации.
- Не добавлять новые UI selectors или менять layout status picker.
- Не запускать `/storm:full-cycle` и не пересоздавать product artifacts.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/StormBdd/TaskStatusCompletionBlockStepDefinitions.cs` -> step definitions `SD-0055..SD-0058` для `SC-0002-002`.
- `src/Unlimotion.Test/StormTaskStatusCompletionBlockExecutableSpecTests.cs` -> парсинг `SC-0002-002`, проверка tags и запуск steps.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> test-only context/result поля для передачи evidence между steps.
- `docs/product/storm.json` -> связи `SC-0002-002 -> TS-0040 -> SD-0055..SD-0058`, metrics и validation evidence.
- `docs/product/reports/*` -> обновить `/storm:cover`, `/storm:bdd-sync`, `/storm:bdd-lint`.

### 6.2 Детальный дизайн
- Step `Дано`: фиксирует наличие актуального набора задач для story context.
- Step `И`: подтверждает, что scenario относится к `ST-0002`.
- Step `Когда`: выполняет test-only проверки:
  - domain manager отклоняет `Completed`, если у задачи есть незавершённая вложенная задача;
  - domain manager отклоняет `Completed`, если completion criterion не удовлетворён;
  - ViewModel выключает `Completed` option при невыполненном criterion;
  - попытка выбрать disabled `Completed` option не меняет status.
- Step `Тогда`: подтверждает, что обе причины блокировки сохранили status `Prepared`, не выставили `CompletedDateTime`, а ViewModel не показывает `Completed` среди available transitions.
- Visual planning artifact: `Не применимо`; layout/visual flow не меняется. State схема: `Prepared + incomplete child OR unsatisfied criterion -> Completed unavailable -> status remains Prepared`.
- UI test video evidence: fallback; текущий Avalonia.Headless/TUnit workflow не сохраняет видео. Next-best evidence = targeted headless UI checks and full `Unlimotion.Test` log.
- Границы поведения: добавляется executable test layer и artifact sync; production behavior меняется только если targeted test выявит реальный дефект, тогда нужен отдельный stability/bug SPEC.

## 7. Бизнес-правила / Алгоритмы
- Задача не может перейти в `Completed`, если completion criteria существуют и хотя бы один criterion не удовлетворён.
- Задача не может перейти в `Completed`, если вложенные задачи ещё incomplete.
- Заблокированный переход должен сохранять исходный status и не создавать `CompletedDateTime`.
- UI должен предлагать пользователю только доступные status transitions.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario("features/storm/st-0002-task-lifecycle.feature", "SC-0002-002")`.
- `StormScenarioRunner` сопоставляет четыре Gherkin steps с `TaskStatusCompletionBlockStepDefinitions`.
- Проверяемые contracts: `TaskTreeManager.UpdateTask`, `TaskItemViewModel.StatusOptions`, `TaskItemViewModel.AvailableStatusTransitionOptions`, `TaskItemViewModel.StatusOption`.

## 9. Изменения модели данных / состояния
- Production data/state: не меняется.
- Test-only context: добавить результат проверки `TaskStatusCompletionBlockScenarioResult`.
- STORM artifact: добавить `TS-0040`, `SD-0055..SD-0058`, обновить `SC-0002-002`, `AC-0005`, `GR-005`, `ST-0002`, metrics/reports.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется.
- Rollout: обычный test/artifact commit.
- Rollback: удалить новый executable spec/step definitions и откатить links/metrics `SC-0002-002`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `SC-0002-002` исполняется через repo-local step definitions.
  - Новый тест подтверждает tags `@scenario:SC-0002-002`, `@story:ST-0002`, `@test:TS-0003`, `@test:TS-0005`.
  - Новый test-only contract проверяет blocked `Completed` для unavailable task и unsatisfied completion criteria.
  - Existing UI evidence для status picker проходит.
  - STORM validator проходит без errors.
- Команды проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal /nr:false`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskStatusCompletionBlockExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TaskStatusTransitionTests/HandleTaskStatusChange_CompletedTaskWithUnsatisfiedCriteria_IsRejected" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TaskStatusTransitionTests/TaskItemViewModel_StatusOptions_DisablesCompletedWhenCriteriaUnsatisfied" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTaskStatusIconUiTests/TaskStatusPickerFlyout_EnablesCompletedOptionAfterCriterionIsSatisfied" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - full `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed`
- Stop rules: если targeted evidence требует production behavior change, остановиться и оформить отдельный bug/stability SPEC.

## 12. Риски и edge cases
- Риск: BDD step будет проверять internal implementation вместо product behavior. Смягчение: проверять observable status, `CompletedDateTime`, available transitions.
- Риск: duplicated shared step text увеличит validator warnings. Смягчение: это intentional reuse; validator должен оставаться без errors.
- Риск: UI video evidence отсутствует. Смягчение: зафиксирован fallback с Avalonia.Headless targeted tests.
- Риск: full suite выявит unrelated order-dependent blocker. Смягчение: создать отдельную SPEC только если blocker мешает validation gate.

## 13. План выполнения
1. Создать SPEC и выполнить post-SPEC review.
2. Добавить test-only BDD result/context/step definitions и executable spec.
3. Обновить `storm.json` и reports через `/storm:bdd-sync`/`/storm:bdd-lint` по текущей структуре.
4. Запустить targeted tests, STORM validator, diff checks и full suite.
5. Выполнить post-EXEC review, исправить findings и закоммитить результат.

## 14. Открытые вопросы
Нет блокирующих. `SC-0002-003` остаётся следующим кандидатом `ST-0002`.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля: сохраняется chain `Story -> AC -> Rule -> Scenario -> Test -> Step Definition -> Code`; Gherkin не заменяет AC; `/storm:bdd-implement` идёт через QUEST; UI-facing state покрывается existing Avalonia.Headless tests; product artifacts на русском.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/StormBdd/TaskStatusCompletionBlockStepDefinitions.cs` | Новый test-only step definition набор | Исполнить `SC-0002-002` |
| `src/Unlimotion.Test/StormTaskStatusCompletionBlockExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result поля | Передать evidence между steps |
| `docs/product/storm.json` | Добавить `TS-0040`, `SD-0055..SD-0058`, links/metrics | `/storm:bdd-sync` |
| `docs/product/reports/coverage.md` | Обновить behavior coverage | `/storm:cover` report |
| `docs/product/reports/bdd-sync.md` | Обновить sync report | `/storm:bdd-sync` |
| `docs/product/reports/bdd-lint.md` | Обновить lint report | `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0002-002` | `automated`, linked tests, без step definitions | `passing`, linked tests + executable BDD test + step definitions |
| Behavior coverage | `14/45` scenarios with step definitions | `15/45` scenarios with step definitions |
| `ST-0002` executable coverage | `1/3` scenarios | `2/3` scenarios |
| Product behavior | Completed блокируется existing logic | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: покрыть сразу `SC-0002-002` и `SC-0002-003`. Плюсы: быстрее закрыть story. Минусы: смешивает negative path и migration regression. Отклонено.
- Вариант: изменить existing UI tests и annotations. Плюсы: меньше новых файлов. Минусы: нарушает ограничение на existing annotations и хуже traceability. Отклонено.
- Выбранный вариант: один scenario-first executable BDD slice, минимальный риск и понятный audit trail.

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
| 1. Ясность цели и границ | 5 | Один scenario-first negative path slice |
| 2. Понимание текущего состояния | 5 | Указаны scenario, AC, rule, tests, manager and ViewModel contracts |
| 3. Конкретность целевого дизайна | 5 | Перечислены файлы, steps, checks and artifact sync |
| 4. Безопасность (миграция, откат) | 5 | Нет schema/UX migration; rollback локальный |
| 5. Тестируемость | 5 | Targeted BDD/domain/UI, validator and full suite |
| 6. Готовность к автономной реализации | 5 | Нет открытых блокеров |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-29-storm-sc0002-completed-block-bdd.md`, central stack (`routing-matrix`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `storm-product-development`), local `AGENTS.override.md`, `SC-0002-002`, `AC-0005`, `GR-005`, `TaskTreeManager`, `TaskItemViewModel`, existing status tests
- Decision: можно выполнять; active goal задаёт автоматическое подтверждение SPEC
- Review passes:
  - Scope/Evidence pass: сверены `storm.json`, feature file, existing tests and manager/ViewModel contracts.
  - Contract pass: spec не требует production behavior changes, не меняет AC, feature wording или annotations.
  - Adversarial risk pass: риск проверки internals снижен через observable status/date/available transitions; video fallback явно указан.
  - Re-review after fixes / Fix and re-review: исправления не потребовались.
  - Stop decision: PASS.
- Evidence inspected: `features/storm/st-0002-task-lifecycle.feature`, `TaskTreeManager.CanTransitionToStatus`, `TaskItemViewModel.CanTransitionToStatus`, `TaskStatusTransitionTests`, `MainControlTaskStatusIconUiTests`.
- Depth checklist:
  - Scope drift / unrelated changes: planned files ограничены BDD test layer + STORM artifacts.
  - Acceptance criteria: прямо связан с `AC-0005`.
  - Validation evidence: targeted, UI, validator, diff and full suite commands указаны.
  - Unsupported claims: runtime/release claims отсутствуют.
  - Regression / edge case: unavailable task and unsatisfied criteria separated.
  - Comments/docs/changelog: changelog не требуется.
  - Hidden contract change: product behavior не меняется; если понадобится, stop rule требует отдельную SPEC.
  - Manual-review challenge: reviewer проверил бы, что BDD step не проходит только по tag checks, а реально блокирует `Completed`.
- No-findings justification: scope малый, owner-doc requirements покрыты, open questions отсутствуют.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | UI video evidence отсутствует для Avalonia.Headless workflow | Использовать fallback: targeted headless UI test output + full suite log | accepted-risk |

- Fixed before continuing: не требуется
- Checks rerun: ручная SPEC linter/rubric
- Needs human: нет; active goal задаёт auto approval
- Residual risks / follow-ups: `SC-0002-003` остаётся отдельным slice

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, `git diff --stat`, relevant diff for `StormStepDefinition`, new BDD step/test files, `docs/product/storm.json`, `docs/product/reports/*`, targeted test output, STORM validator output, full-suite log `C:\tmp\unlimotion-full-suite-sc0002-completed-block-bdd.log`
- Decision: можно завершать и коммитить
- Review passes:
  - Scope/Evidence pass: проверены все planned files; production code, `.feature`, project files, workflows and existing test annotations не менялись.
  - Contract pass: `SC-0002-002` получил `TS-0040` and `SD-0055..SD-0058`; `TS-0003`/`TS-0005` сохранены; `AC-0005` поднят до full coverage на executable layer.
  - Adversarial risk pass: BDD step проверяет observable outcomes (`Prepared`, `CompletedDateTime = null`, unavailable transition), а не только tags; unavailable task проверяется через incomplete child graph.
  - Re-review after fixes / Fix and re-review: после artifact full-suite evidence sync повторены STORM validator и diff checks.
  - Stop decision: PASS.
- Evidence inspected:
  - `StormTaskStatusCompletionBlockExecutableSpecTests` прошло 1/1.
  - `TaskStatusTransitionTests/HandleTaskStatusChange_CompletedTaskWithUnsatisfiedCriteria_IsRejected` прошло 1/1.
  - `TaskStatusTransitionTests/TaskItemViewModel_StatusOptions_DisablesCompletedWhenCriteriaUnsatisfied` прошло 1/1.
  - `MainControlTaskStatusIconUiTests/TaskStatusPickerFlyout_EnablesCompletedOptionAfterCriterionIsSatisfied` прошло 1/1.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 6 warnings.
  - Full `Unlimotion.Test` прошёл 571/571 вне managed sandbox.
- Depth checklist:
  - Scope drift / unrelated changes: изменений вне planned BDD/spec/artifact files нет.
  - Acceptance criteria: `AC-0005` закрыт через `SC-0002-002 -> TS-0040 -> SD-0055..SD-0058`.
  - Validation evidence: targeted, UI, validator, full suite and diff checks covered.
  - Unsupported claims: runtime/release claims отсутствуют.
  - Regression / edge case: incomplete child and unsatisfied criterion checked separately.
  - Comments/docs/changelog: новых code comments нет; changelog не требуется.
  - Hidden contract change: production behavior не менялся.
  - Manual-review challenge: reviewer мог бы спросить, почему `IsCanBeCompleted=false` не используется как main unavailable evidence; ответ: actual manager completeness rule is child/blocker graph, covered by incomplete child.
- No-findings justification: diff соответствует spec, проверки покрывают BDD/domain/UI/full gates, validator без errors.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | UI video evidence отсутствует для Avalonia.Headless workflow | Использован fallback: targeted headless UI test output + full-suite log | accepted-risk |

- Fixed before final report: pending full-suite evidence заменён на 571/571 в `storm.json` и reports
- Checks rerun: STORM validator, `git diff --check`, trailing whitespace scan
- Validation evidence: listed above
- Unrelated changes: нет
- Needs human: нет
- Residual risks / follow-ups: `SC-0002-003` остаётся следующим `/storm:cover` slice

## Approval
Автоматически подтверждено активной целью пользователя: "я автоматически спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0002-002` | 0.91 | Нет | Перейти к EXEC | Нет | Да, active goal задаёт auto approval | Один negative-path slice продолжает `ST-0002` без scope drift | `specs/2026-06-29-storm-sc0002-completed-block-bdd.md` |
| EXEC | /storm:bdd-implement SC-0002-002 | 0.93 | Нет | Commit после финальных checks | Нет | Да, active goal задаёт auto approval | Scenario получил executable BDD links; targeted/domain/UI/full gates passed | src/Unlimotion.Test/StormTaskStatusCompletionBlockExecutableSpecTests.cs; src/Unlimotion.Test/StormBdd/TaskStatusCompletionBlockStepDefinitions.cs; docs/product/storm.json; docs/product/reports/* |
