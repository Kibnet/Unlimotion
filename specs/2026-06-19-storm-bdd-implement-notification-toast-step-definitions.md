# Исполняемые step definitions для notification error toast

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST SPEC / `/storm:bdd-implement SC-0016-001`
- Владелец: Codex + product owner approval gate
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка `fdba/Unlimotion`
- Ограничения: product artifacts на русском; production code, `.feature` wording и существующие test annotations не менять; EXEC только после фразы `Спеку подтверждаю`
- Связанные ссылки: `docs/product/storm.json`, `features/storm/st-0016-notification-error-ux.feature`, `src/Unlimotion.Test/ToastNotificationUiTests.cs`, `docs/product/reports/ranking.md`

## 1. Overview / Цель

Добавить следующий узкий executable BDD slice для `SC-0016-001`: сценарий "Ошибка операции показывается в toast и закрывается пользователем" должен исполняться из `.feature` текста через repo-local STORM BDD runner и переиспользовать существующее headless UI evidence `TS-0021`.

Outcome contract:
- Success means: `SC-0016-001` получает `SD-0017..SD-0021`, новый executable spec `TS-0030` проходит, `TS-0021` продолжает проходить.
- Итоговый артефакт / output: test-only BDD slice + синхронизированные `docs/product/storm.json` и reports.
- Stop rules: остановиться, если нужно менять production code, `.feature` wording, acceptance criteria, существующие test annotations, UI layout/behavior или подключать внешний BDD framework.

## 2. Текущее состояние (AS-IS)

- `SC-0016-001` уже есть в `features/storm/st-0016-notification-error-ux.feature`, имеет статус `@passing` и linked test `TS-0021`.
- `TS-0021` указывает на `ToastNotificationUiTests.MainScreen_ErrorToast_RendersAndCloseButtonRemovesMessage`.
- Тест запускает Avalonia Headless, создает `MainScreen`, вызывает `NotificationManagerWrapper.ErrorToast("Toast failure")`, проверяет toast text и закрытие кнопкой `ToastNotificationErrorCloseButton`.
- Перед EXEC `storm.json` уже имел четыре step-executable scenarios после предыдущих BDD slices.
- Passing scenarios без step definitions: `SC-0011-001`, `SC-0011-002`, `SC-0016-001`. Для следующего slice выбран `SC-0016-001`, потому что он один, связан с уже изолированным UI test и не требует серверной, RavenDB, SignalR или HTTP инфраструктуры.

## 3. Проблема

`SC-0016-001` покрыт обычным UI test evidence, но не исполняется как BDD scenario из `.feature` файла. Из-за этого traceability chain неполная: `Scenario -> Test` есть, а `Scenario -> Step Definition -> Test contract` отсутствует.

## 4. Цели дизайна

- Разделение ответственности: existing UI checks вынести в reusable test contract, step definitions только связывают Gherkin wording с contract assertions.
- Повторное использование: `ToastNotificationUiTests` и новый executable spec используют один contract.
- Тестируемость: новый `StormNotificationToastExecutableSpecTests` читает `.feature` файл и проверяет exact step ids.
- Консистентность: сохранить repo-local `StormBdd` style, ID sequence и artifact sync pattern.
- Обратная совместимость: не менять production behavior, `.feature` wording и existing test annotations.

## 5. Non-Goals

- Не менять UI layout, визуальный дизайн toast, automation ids или поведение notification manager.
- Не добавлять success-toast/localization coverage.
- Не расширять `ST-0011` server-storage scenarios.
- Не запускать Browser/Playwright или desktop video capture.
- Не подключать внешний Cucumber/SpecFlow/BDD framework.
- Не коммитить изменения без отдельной команды пользователя.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `src/Unlimotion.Test/ToastNotificationUiContract.cs` -> reusable headless UI assertion for error toast rendering and close behavior.
- `src/Unlimotion.Test/ToastNotificationUiTests.cs` -> сохраняет существующий `[Test]` method и делегирует contract helper.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> добавляет минимальные поля контекста для notification toast scenario result.
- `src/Unlimotion.Test/StormBdd/NotificationToastStepDefinitions.cs` -> регистрирует `SD-0017..SD-0021` для пяти шагов `SC-0016-001`.
- `src/Unlimotion.Test/StormNotificationToastExecutableSpecTests.cs` -> парсит `features/storm/st-0016-notification-error-ux.feature`, исполняет scenario through runner, проверяет executed step ids.
- `docs/product/storm.json` и reports -> добавляют `TS-0030`, `SD-0017..SD-0021`, обновляют behavior coverage metrics до `5/45`.

### 6.2 Детальный дизайн

Поток:
1. Executable spec парсит `SC-0016-001` из `.feature`.
2. Runner исполняет exact Gherkin steps:
   - `SD-0017`: `Дано у пользователя открыт основной экран Unlimotion`
   - `SD-0018`: `И поведение относится к истории ST-0016`
   - `SD-0019`: `Когда операция сообщает ошибку через notification manager`
   - `SD-0020`: `Тогда пользователь видит toast с текстом ошибки`
   - `SD-0021`: `И пользователь может закрыть toast без перезапуска экрана`
3. Contract запускает existing Avalonia Headless flow and returns result flags:
   - main screen opened;
   - error operation reported;
   - toast text observed;
   - close button removed toast.
4. Then/And steps assert result flags.

Visual planning artifact для UI-facing изменений: `Не применимо`. Эта SPEC не меняет visual layout/flow/state продукта; она только делает уже существующий headless UI behavior executable through BDD. Fallback state description: на основном экране появляется error toast с текстом ошибки, затем пользователь нажимает close button, после чего toast исчезает без перезапуска экрана.

UI test video evidence: `Не применимо`. Проверка выполняется Avalonia Headless test runner; нет изменения UI behavior или visual design, поэтому достаточно targeted UI test evidence. Если reviewer потребует визуальный артефакт, это отдельная evidence task, а не часть текущего BDD slice.

## 7. Бизнес-правила / Алгоритмы

- Ошибка операции должна быть видна как in-app toast внутри основного экрана.
- Пользователь должен иметь возможность закрыть toast без перезапуска экрана.
- BDD step definitions не должны утверждать success-toast behavior, потому что `SC-0016-001` покрывает только error toast.

## 8. Точки интеграции и триггеры

- Интеграция с `StormFeatureParser` и `StormScenarioRunner`.
- Интеграция с Avalonia Headless через existing `HeadlessUnitTestSession`.
- Триггер behavior в test contract: `NotificationManagerWrapper.ErrorToast("Toast failure")`.

## 9. Изменения модели данных / состояния

- Persisted data не меняется.
- Runtime product state не меняется.
- Test-only state: `StormScenarioContext` получает поля для notification toast scenario result.
- Product artifact state: `storm.json` получает новые `TS-0030`, `SD-0017..SD-0021` и metric updates.

## 10. Миграция / Rollout / Rollback

- Rollout: test-only BDD slice, не влияет на runtime.
- Rollback: удалить новый executable spec, step definitions, contract extraction and artifact links; восстановить `ToastNotificationUiTests` inline assertions if needed.
- Обратная совместимость: existing `TS-0021` method name, annotations and behavior сохраняются.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
- `SC-0016-001` получает исполняемые step definitions для всех пяти Gherkin steps.
- Новый executable spec запускает scenario из `features/storm/st-0016-notification-error-ux.feature`.
- `ToastNotificationUiTests` сохраняет existing test annotation attributes and passes.
- `storm.json` и reports отражают `TS-0030`, `SD-0017..SD-0021` и `5/45` step-executable scenarios.
- Production code, `.feature` wording, acceptance criteria and existing test annotations не меняются.

Команды проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormNotificationToastExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ToastNotificationUiTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-19-storm-bdd-implement-notification-toast-step-definitions.md
```

Stop rules для validation loops:
- Если Avalonia Headless flaky/fails из-за shared UI state, не менять production UI; сначала проверить isolation attributes and existing `SharedUiStateParallelLimit`.
- Если scenario execution требует `.feature` wording changes, остановиться и предложить отдельный artifact-sync SPEC.
- Если нужно менять toast UI behavior, остановиться и предложить отдельный UI delivery-task.

## 12. Риски и edge cases

- Риск: BDD step definitions начнут повторять UI mechanics вместо product outcome.
  - Смягчение: steps остаются декларативными, mechanics hidden inside test contract.
- Риск: Avalonia Headless test требует serialized UI execution.
  - Смягчение: новый executable spec должен использовать same `[NotInParallel("AvaloniaHeadless")]` and `[ParallelLimiter<SharedUiStateParallelLimit>]`.
- Риск: success-toast optional gap попадет в scope.
  - Смягчение: explicitly out of scope; current scenario only error toast.

## 13. План выполнения

1. Вынести current deterministic headless assertions из `ToastNotificationUiTests` в `ToastNotificationUiContract`.
2. Добавить context fields/result record for notification toast BDD scenario.
3. Добавить `NotificationToastStepDefinitions` with `SD-0017..SD-0021`.
4. Добавить `StormNotificationToastExecutableSpecTests`.
5. Запустить build and targeted tests.
6. Синхронизировать `storm.json` and reports through `/storm:bdd-sync` and `/storm:bdd-lint`.
7. Запустить artifact validation and hygiene checks.

## 14. Открытые вопросы

Блокирующих вопросов нет. Выбранный кандидат основан на `docs/product/reports/ranking.md` and current `storm.json`.

## 15. Соответствие профилю

- Профиль: `storm-product-development` + `delivery-task` через QUEST.
- Выполненные требования профиля:
  - Gherkin не заменяет acceptance criteria.
  - `Scenario -> Test -> Step Definition -> Code` будет синхронизирован.
  - `/storm:bdd-implement` не стартует без SPEC approval.
  - Production code/test annotations остаются неизменными.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/ToastNotificationUiContract.cs` | new reusable test contract | Переиспользовать existing TS-0021 evidence в BDD step definitions |
| `src/Unlimotion.Test/ToastNotificationUiTests.cs` | delegate existing test to contract | Сохранить TS-0021 и убрать дублирование |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | context fields/result | Передать результат между Gherkin steps |
| `src/Unlimotion.Test/StormBdd/NotificationToastStepDefinitions.cs` | `SD-0017..SD-0021` | Исполнить `SC-0016-001` |
| `src/Unlimotion.Test/StormNotificationToastExecutableSpecTests.cs` | new executable spec `TS-0030` | Запуск scenario из `.feature` |
| `docs/product/storm.json` | traceability and metrics sync | `TS-0030`, `SD-0017..SD-0021`, `5/45` |
| `docs/product/reports/*.md` | coverage/sync/lint/rank/trace updates | Отразить BDD evidence |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| `SC-0016-001.step_definitions` | `[]` | `SD-0017..SD-0021` |
| `SC-0016-001.linked_tests` | `TS-0021` | `TS-0021`, `TS-0030` |
| Step-executable scenarios | previous BDD state | `5/45` |
| Existing UI test | inline assertions | same test method delegates to contract |

## 18. Альтернативы и компромиссы

- Вариант A: реализовать `SC-0011-001` server auth BDD slice.
  - Плюсы: более глубокий backend contract.
  - Минусы: больше инфраструктуры и риск затронуть server/RavenDB setup.
- Вариант B: реализовать `SC-0016-001` notification toast BDD slice.
  - Плюсы: маленький изолированный UI evidence, один scenario, высокий ranking item `CV-0006`.
  - Минусы: требует Avalonia Headless serialization.
- Выбран Вариант B, потому что это минимальный следующий executable slice вне `ST-0014` с already passing test evidence and low blast radius.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals конкретны. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, поток, integration points, data impact и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Stop rules, rollback и ограничения production/UI behavior зафиксированы. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, targeted commands and file table есть. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, альтернативы и review заполнены; блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST route and Russian artifacts соблюдены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope narrow: one scenario, one existing UI test, one BDD slice. |
| 2. Понимание текущего состояния | 5 | Сверены feature, storm.json, ranking and TS-0021 test file. |
| 3. Конкретность целевого дизайна | 5 | Planned files, IDs, flow and commands are explicit. |
| 4. Безопасность (миграция, откат) | 5 | Production/UI behavior unchanged; rollback path listed. |
| 5. Тестируемость | 5 | Build, targeted executable spec, existing UI test and artifact validator listed. |
| 6. Готовность к автономной реализации | 5 | No blocking questions; stop conditions concrete. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-19-storm-bdd-implement-notification-toast-step-definitions.md`; instruction stack: central `AGENTS.md`, `routing-matrix.md`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `storm-product-development`, local `AGENTS.override.md`; selected profile: `storm-product-development`; open questions: none; planned changed files listed in section 16.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: inspected `ranking.md`, `coverage.md`, `storm.json`, `st-0016-notification-error-ux.feature`, `ToastNotificationUiTests.cs`.
  - Contract pass: no production code, no `.feature` wording, no existing test annotations; new test-only BDD slice only.
  - Adversarial risk pass: checked UI behavior drift, success-toast scope creep, Avalonia Headless serialization, server-storage distraction.
  - Re-review after fixes / Fix and re-review: included visual planning fallback and UI video evidence rationale for UI-facing spec.
  - Stop decision: PASS, approval can be requested.
- Evidence inspected:
  - `features/storm/st-0016-notification-error-ux.feature`
  - `src/Unlimotion.Test/ToastNotificationUiTests.cs`
  - `docs/product/reports/ranking.md`
  - `docs/product/reports/coverage.md`
- Depth checklist:
  - Scope drift / unrelated changes: blocked by Non-Goals and Stop rules.
  - Acceptance criteria: scenario AC is preserved and executable evidence will be additive.
  - Validation evidence: commands listed.
  - Unsupported claims: runtime visual changes and success-toast coverage explicitly not claimed.
  - Regression / edge case: existing TS-0021 rerun required.
  - Comments/docs/changelog: no changelog required for test-only local slice.
  - Hidden contract change: none planned; contract extraction preserves assertions.
  - Manual-review challenge: reviewer may ask whether UI video is required; spec documents why headless evidence is sufficient because UI behavior is unchanged.
- No-findings justification: SPEC is a narrow additive BDD executable slice using existing evidence, with no unresolved product or architecture choice.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | UI video evidence is omitted. | Keep explicit fallback rationale and use targeted Avalonia Headless evidence. | accepted-risk |

- Fixed before continuing: added visual planning and UI video evidence rationale.
- Checks rerun: manual spec-linter/spec-rubric/post-SPEC review.
- Needs human: approval to move from SPEC to EXEC.
- Residual risks / follow-ups: success-toast/localization remains optional future expansion, not part of this slice.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, targeted test output, `storm.json` validation, hygiene checks, relevant changed files.
- Decision: можно завершать EXEC.
- Review passes:
  - Scope/Evidence pass: changed files match section 16; production code, `.feature` wording and existing test annotations were not changed.
  - Contract pass: `TS-0021` remains the existing UI test; `TS-0030` adds executable BDD trace for the same behavior.
  - Adversarial risk pass: checked success-toast scope creep, UI behavior drift, and accidental runtime claim expansion.
  - Re-review after fixes / Fix and re-review: removed stale pre-EXEC metric literals from SPEC hygiene search.
  - Stop decision: PASS, no blocker.
- Evidence inspected:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormNotificationToastExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ToastNotificationUiTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated production changes.
  - Acceptance criteria: all five `SC-0016-001` steps executable.
  - Validation evidence: build and targeted tests passed.
  - Unsupported claims: no runtime/release UI claim added.
  - Regression / edge case: existing `TS-0021` rerun passed.
  - Comments/docs/changelog: no changelog needed for local test-only slice.
  - Hidden contract change: none; contract extraction preserves the same assertions.
  - Manual-review challenge: reviewer may ask whether `ToastNotificationUiContract` hides UI mechanics; acceptable because Gherkin stays product-level and existing UI evidence remains.
- No-findings justification: EXEC is additive, tests pass, and artifacts validate with only the pre-existing documented duplicate Telegram Given warning.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | bdd-lint | Existing duplicate step text warning for `SD-0009`/`SD-0013` remains. | Keep documented warning; not related to `SC-0016-001`. | accepted-risk |

- Fixed before final report: removed stale metric literals from SPEC search surface.
- Checks rerun: build, targeted TUnit tests, artifact validator, old-metric search, `git diff --check`, trailing-space scan.
- Validation evidence: all required commands passed; artifact validator returned 0 errors and 1 known warning.
- Unrelated changes: none observed.
- Needs human: no.
- Residual risks / follow-ups: `SC-0011-001` and `SC-0011-002` remain passing scenarios without step definitions.

## Approval

Ожидается фраза: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего BDD slice | 0.9 | Нет | Создать SPEC для `SC-0016-001` | Нет | Нет | `SC-0016-001` является passing scenario без step definitions, с изолированным existing UI evidence. | `docs/product/reports/ranking.md`, `docs/product/storm.json`, `features/storm/st-0016-notification-error-ux.feature` |
| SPEC | Подготовка SPEC и review | 0.92 | Нет | Запросить подтверждение пользователя | Да | Нет | `/storm:bdd-implement` меняет tests/artifacts, поэтому нужен QUEST gate. | `specs/2026-06-19-storm-bdd-implement-notification-toast-step-definitions.md` |
| EXEC | Подтверждение SPEC | 1.0 | Нет | Добавить test-only BDD slice | Нет | Да: пользователь написал `Спеку подтверждаю` | QUEST gate открыт, можно менять planned files в рамках спеки. | `specs/2026-06-19-storm-bdd-implement-notification-toast-step-definitions.md` |
| EXEC | Реализация и синхронизация | 0.9 | Нет | Завершить итоговый отчёт | Нет | Нет | `SC-0016-001` получил `TS-0030`, `SD-0017..SD-0021`, targeted tests and artifact validation passed. | `src/Unlimotion.Test/*`, `docs/product/*` |
