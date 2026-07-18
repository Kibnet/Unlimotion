# STORM SC-0002-001: executable BDD для поддерживаемых статусов

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая ветка `storm-bootstrap`
- Ограничения: не менять production behavior; не менять acceptance criteria; не удалять существующие stories/tests/conflicts; сохранить русскоязычные продуктовые артефакты
- Связанные ссылки: `docs/product/storm.json`, `docs/product/reports/*`, `SC-0002-001`, `ST-0002`, `AC-0004`, `TS-0003`, `TS-0005`

## 1. Overview / Цель
Добавить executable BDD layer для сценария `SC-0002-001`: "Поддерживаются статусы NotReady, Prepared, InProgress, Completed и Archived." Сценарий уже связан с автоматизированными тестами, но не имеет repo-local step definitions.

Outcome contract:
- Success means: `SC-0002-001` имеет новый automated test, step definitions, passing targeted evidence и синхронизированные STORM artifacts.
- Итоговый артефакт / output: test-only executable spec + обновлённые `storm.json` и reports.
- Stop rules: остановиться, если для прохождения нужны изменения production code, изменение `.feature` wording, test annotations или продуктового поведения.

## 2. Текущее состояние (AS-IS)
- `docs/product/storm.json` содержит `SC-0002-001` со статусом `automated`, связями `TS-0003` и `TS-0005`, но `step_definitions: []`.
- `TaskStatus` уже содержит пять значений: `NotReady`, `Prepared`, `InProgress`, `Completed`, `Archived`.
- `TaskStatusOption.All` уже отображает те же пять статусов для ViewModel/UI.
- `MainControlTaskStatusIconUiTests` уже содержит UI evidence для flyout и status history.
- `TaskStatus*Tests` уже содержат domain/storage/migration evidence.
- Существующий BDD harness живёт в `src/Unlimotion.Test/StormBdd/*` и парсит scenarios из `storm.json`.

## 3. Проблема
`SC-0002-001` участвует в `/storm:cover`, но его Gherkin steps не являются исполняемыми. Из-за этого behavior coverage зависит от linked existing tests, а не от прямой цепочки `Scenario -> Test -> Step Definition -> Code`.

## 4. Цели дизайна
- Разделение ответственности: оставить product wording в `storm.json`, а исполняемый мост держать в test-only `StormBdd`.
- Повторное использование: использовать существующий `StormScenarioRunner` и текущие domain/UI contracts.
- Тестируемость: один новый executable spec должен падать при несовпадении текста steps, статусов domain/ViewModel или базового UI transition evidence.
- Консистентность: сохранить стиль уже созданных `Storm*ExecutableSpecTests`.
- Обратная совместимость: не менять production API, persisted model, selectors или behavior.

## 5. Non-Goals
- Не менять `TaskStatus`, `TaskStatusOption`, UI layout или status transition rules.
- Не менять существующие тесты TS-0003/TS-0005 и их annotations.
- Не переводить все `ST-0002` scenarios за один раз.
- Не запускать `/storm:full-cycle` и не пересоздавать артефакты.
- Не добавлять video artifacts: изменение test-only, без нового UI behavior; fallback evidence = Avalonia.Headless targeted tests.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/StormBdd/TaskStatusSupportStepDefinitions.cs` -> step definitions `SD-0051..SD-0054` для `SC-0002-001`.
- `src/Unlimotion.Test/StormTaskStatusSupportExecutableSpecTests.cs` -> парсинг `SC-0002-001`, проверка tags, запуск steps.
- `docs/product/storm.json` -> связи `SC-0002-001 -> TS-0039 -> SD-0051..SD-0054`, статус/evidence.
- `docs/product/reports/*` -> `/storm:bdd-sync`, `/storm:bdd-lint`, behavior coverage metrics.

### 6.2 Детальный дизайн
- Step `Дано`: фиксирует наличие актуального набора задач для story context.
- Step `И`: подтверждает, что scenario относится к `ST-0002`.
- Step `Когда`: выполняет test-only проверки:
  - `TaskStatus` enum равен пяти ожидаемым статусам и порядку.
  - `TaskStatusOption.All` содержит те же статусы.
  - `TaskStatusFilter.GetDefinitions()` создаёт фильтры для всех статусов.
- Step `Тогда`: подтверждает результат и дополнительно использует existing UI evidence через focused checks в новом executable spec или linked targeted UI run.
- Visual planning artifact: Не применимо, потому что UI behavior и визуальная компоновка не меняются.
- UI test video evidence: fallback, потому что Avalonia.Headless test runner не сохраняет видео в текущем workflow; next-best evidence = targeted headless test output.
- Границы поведения: только test artifacts и STORM artifacts.

## 7. Бизнес-правила / Алгоритмы
- Поддерживаемый набор статусов для `AC-0004`: `NotReady`, `Prepared`, `InProgress`, `Completed`, `Archived`.
- Domain, ViewModel options и status filters не должны расходиться по набору статусов.
- `SC-0002-001` должен оставаться linked к `TS-0003` и `TS-0005`; новый executable BDD test добавляется как дополнительное evidence.

## 8. Точки интеграции и триггеры
- Новый executable spec запускается TUnit в составе `Unlimotion.Test`.
- `StormFeatureParser.ParseScenario("features/storm/st-0002-task-lifecycle.feature", "SC-0002-001")` читает scenario из `storm.json`.
- `StormScenarioRunner` сопоставляет Gherkin steps с `TaskStatusSupportStepDefinitions`.

## 9. Изменения модели данных / состояния
- Production data/state: не меняется.
- Test-only context: добавить поля результата для task-status support scenario в `StormScenarioContext`, если они нужны для передачи между steps.
- STORM artifact: добавить `TS-0039` и `SD-0051..SD-0054`, обновить scenario links/metrics.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется.
- Rollout: обычный test-only commit.
- Rollback: удалить новый executable spec/step definitions и откатить STORM artifact links для `SC-0002-001`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `SC-0002-001` исполняется через repo-local step definitions.
  - Новый тест подтверждает tags `@scenario:SC-0002-001`, `@test:TS-0003`, `@test:TS-0005`.
  - Новый test-only contract проверяет пять статусов в domain/ViewModel/filter layer.
  - Existing UI evidence для status picker проходит.
  - STORM validator проходит без errors.
- Команды проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal /nr:false`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskStatusSupportExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTaskStatusIconUiTests/TaskStatusPickerFlyout_ExposesOnlyAvailableTransitionOptions" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTaskStatusIconUiTests/TaskStatusPicker_SelectingStatusOption_UpdatesTaskStatusHistory" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - полный `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed`
- Stop rules: любые production code changes требуют отдельной SPEC.

## 12. Риски и edge cases
- Риск: test-only BDD дублирует existing unit/UI evidence. Смягчение: step definitions проверяют только product contract и не подменяют существующие детальные tests.
- Риск: feature text изменится и BDD test упадёт. Это ожидаемая защита traceability.
- Риск: full suite нестабилен из-за unrelated UI shared state. Смягчение: targeted evidence обязателен; full suite blocker фиксировать отдельной SPEC, если проявится.

## 13. План выполнения
1. Добавить SPEC и post-SPEC review.
2. Добавить test-only BDD contract/result/step definitions и executable spec.
3. Обновить `storm.json` и reports через `/storm:bdd-sync`/`/storm:bdd-lint` логически по текущей структуре.
4. Запустить targeted tests, STORM validator, diff checks и полный suite.
5. Выполнить post-EXEC review и подготовить commit.

## 14. Открытые вопросы
Нет блокирующих. `SC-0002-002` и `SC-0002-003` остаются следующими `/storm:cover` кандидатами.

## 15. Соответствие профилю
- Профиль: `storm-product-development`
- Выполненные требования профиля: сохраняется chain `Story -> AC -> Scenario -> Test -> Step Definition -> Code`; Gherkin не заменяет AC; `/storm:bdd-implement` идёт как delivery-task через QUEST; product artifacts на русском.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/StormBdd/TaskStatusSupportStepDefinitions.cs` | Новый test-only step definition набор | Исполнить `SC-0002-001` |
| `src/Unlimotion.Test/StormTaskStatusSupportExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result поля при необходимости | Передать evidence между steps |
| `docs/product/storm.json` | Добавить `TS-0039`, `SD-0051..SD-0054`, links/metrics | `/storm:bdd-sync` |
| `docs/product/reports/coverage.md` | Обновить behavior coverage | `/storm:cover` report |
| `docs/product/reports/bdd-sync.md` | Обновить sync report | `/storm:bdd-sync` |
| `docs/product/reports/bdd-lint.md` | Обновить lint report | `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0002-001` | `automated`, linked tests, без step definitions | `passing`, linked tests + executable BDD test + step definitions |
| Behavior coverage | `13/45` scenarios with step definitions | `14/45` scenarios with step definitions |
| Product behavior | Не меняется | Не меняется |

## 18. Альтернативы и компромиссы
- Вариант: покрыть сразу все `ST-0002` scenarios. Плюсы: меньше overhead. Минусы: выше риск смешать разные rules. Отклонено для узкого `/storm:cover`.
- Вариант: только artifact-only отметить existing tests. Плюсы: быстрее. Минусы: не улучшает executable spec ratio. Отклонено, потому что пользователь подтвердил продолжение BDD implementation.
- Выбранный вариант: один scenario-first test-only slice, минимальный риск и хорошая трассируемость.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, data/rollback и boundaries описаны |
| C. Безопасность изменений | 11-13 | PASS | Production behavior не меняется; rollback простой |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria и команды проверки конкретны |
| E. Готовность к автономной реализации | 17-19 | PASS | План малый, блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | STORM/BDD chain и QUEST gate соблюдены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один scenario-first slice |
| 2. Понимание текущего состояния | 5 | Указаны scenario, tests, harness и domain/UI evidence |
| 3. Конкретность целевого дизайна | 5 | Перечислены файлы, steps и checks |
| 4. Безопасность (миграция, откат) | 5 | Нет production миграции; rollback локальный |
| 5. Тестируемость | 5 | Targeted, UI evidence, artifact validator и full suite |
| 6. Готовность к автономной реализации | 5 | Нет открытых блокеров |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-29-storm-sc0002-status-support-bdd.md`, central stack, `storm-product-development`, planned changed files, `SC-0002-001`, `TS-0003`, `TS-0005`
- Decision: можно выполнять; пользователь уже дал переход EXEC фразой `спеку подтверждаю`
- Review passes:
  - Scope/Evidence pass: сверены `storm.json`, existing BDD harness и status tests.
  - Contract pass: spec не требует production changes и сохраняет existing AC.
  - Adversarial risk pass: главный риск full-suite instability вынесен в stop rules.
  - Re-review after fixes / Fix and re-review: исправления не потребовались.
  - Stop decision: PASS.
- Evidence inspected: `SC-0002-001`, `TaskStatus.cs`, `TaskStatusOption.cs`, `MainControlTaskStatusIconUiTests`, existing `Storm*ExecutableSpecTests`.
- Depth checklist:
  - Scope drift / unrelated changes: scope ограничен test-only + product artifacts.
  - Acceptance criteria: прямо связаны с `AC-0004`.
  - Validation evidence: команды указаны.
  - Unsupported claims: runtime/release support не заявляется.
  - Regression / edge case: feature text drift и full-suite instability учтены.
  - Comments/docs/changelog: changelog не требуется.
  - Hidden contract change: production contract не меняется.
  - Manual-review challenge: reviewer проверил бы, что steps действительно исполняют scenario, а не только tags.
- No-findings justification: scope малый, risks перечислены, проверки воспроизводимы.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence не создаётся для headless UI run | Использовать fallback с targeted Avalonia.Headless tests | accepted-risk |

- Fixed before continuing: не требуется
- Checks rerun: ручная SPEC linter/rubric
- Needs human: нет; approval получен
- Residual risks / follow-ups: `SC-0002-002` и `SC-0002-003` остаются отдельными slices

### Post-EXEC Review
- Статус: PASS
- Scope executed: `SC-0002-001`, `TS-0039`, `SD-0051..SD-0054`, `docs/product/storm.json`, `docs/product/reports/*`.
- Изменения: добавлен repo-local executable BDD test для поддержки пяти статусов; existing `TS-0003` и `TS-0005` сохранены; acceptance criteria не заменялись на Gherkin.
- Evidence:
  - `StormTaskStatusSupportExecutableSpecTests` прошло 1/1.
  - `MainControlTaskStatusIconUiTests` linked status picker checks прошли 2/2.
  - Full `Unlimotion.Test` прошёл 570/570 вне managed sandbox: `C:\tmp\unlimotion-full-suite-sc0002-status-support-bdd-final2.log`.
  - STORM validator проходит без errors; remaining warnings относятся к intentional shared step text.
- Stop rules: не нарушены; `.feature` wording, existing test annotations, project files и workflows не менялись.
- Residual risks / follow-ups: `SC-0002-002` и `SC-0002-003` остаются следующими `/storm:cover` кандидатами для `ST-0002`.

## Approval
Получено в чате: "спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0002-001` | 0.90 | Нет | Перейти к EXEC | Нет | Да, пользователь подтвердил SPEC | Один scenario-first slice снижает риск scope drift | `specs/2026-06-29-storm-sc0002-status-support-bdd.md` |
| EXEC | /storm:bdd-implement SC-0002-001 | 0.93 | Нет | Commit после финальных checks | Нет | Да, SPEC подтверждена | Scenario получил executable BDD links, full-suite gate восстановлен через отдельный stability sub-scope | src/Unlimotion.Test/StormTaskStatusSupportExecutableSpecTests.cs; src/Unlimotion.Test/StormBdd/TaskStatusSupportStepDefinitions.cs; docs/product/storm.json; docs/product/reports/* |
