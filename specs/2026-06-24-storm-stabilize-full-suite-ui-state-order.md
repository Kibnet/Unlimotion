# Стабилизация full-suite UI state/order failure

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST SPEC / stabilization follow-up after `/storm:cover`
- Владелец: Codex + product owner approval gate
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка `storm-bootstrap`
- Ограничения: не менять product behavior без доказательства; UI-facing behavior changes require relevant UI tests; EXEC только после фразы `Спеку подтверждаю`
- Связанные ссылки: `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `src/Unlimotion.Test/HeadlessSessionExtensions.cs`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель

Закрыть отдельный validation blocker: previous full `Unlimotion.Test` run failed in full-suite context on `MainControlTreeCommandsUiTests.TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask`, while the same test passed in isolation. Это не новый product behavior gap, но риск для доверия к future `/storm:cover` и `/storm:bdd-implement` validation.

Outcome contract:
- Success means: причина full-suite-only failure найдена и устранена или оформлена как отдельный подтверждённый environment blocker; targeted UI test, affected class/context and practical full-suite validation pass or have a documented stop reason.
- Итоговый output: minimal test infrastructure/test isolation/UI fix, synchronized STORM reports if validation risk status changes.
- Stop rules: остановиться, если требуется product behavior change, broad UI refactor, изменение unrelated tests, изменение production clipboard/selection semantics без targeted failing evidence, или длительная full-suite диагностика начинает требовать отдельной automation/infrastructure task.

## 2. Текущее состояние (AS-IS)

- `/storm:cover` BDD work completed current active behavior gaps: `CV-0001..CV-0006` covered; `CV-0007` remains internal/orphan candidate by product decision Вариант B.
- `docs/product/reports/coverage.md` records remaining validation risk: previous full-suite run failed 561/562 on `MainControlTreeCommandsUiTests.TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask`; the same test passed 1/1 in isolation; sequential full rerun timed out after 15 minutes.
- `MainControlTreeCommandsUiTests` already has `[NotInParallel("AvaloniaHeadless")]` and `[ParallelLimiter<SharedUiStateParallelLimit>]`.
- Failing scenario uses Avalonia Headless, `MainWindowViewModelFixture`, shared mock notification manager, clipboard delegate `vm.GetClipboardTextAsync`, selection in `AllTasksTree`, `Ctrl+V` ignored in text input and `Ctrl+Shift+V` paste under selected task.
- Current evidence is insufficient to say whether the root cause is test isolation, stale headless focus/selection state, fixture data leakage, clipboard delegate leakage, timing/order, or a real UI behavior defect.

## 3. Проблема

Full-suite-only UI failure makes future STORM delivery evidence weaker: targeted BDD slices pass, but the repository cannot yet use full `Unlimotion.Test` as a reliable regression gate. Because the failing test passed in isolation, applying a direct product code fix without reproduction context would be speculative.

## 4. Цели дизайна

- Reproduce first: collect minimal failing order/context before code changes.
- Keep scope narrow: focus on `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` and shared UI/headless state around it.
- Preserve product behavior: if behavior is correct and test isolation is weak, fix the test/helper; if product behavior is wrong, add/update UI test evidence and make minimal production fix.
- Make validation actionable: targeted, class-level and full-suite commands should leave clear evidence.
- Keep STORM trace honest: reports should distinguish validation stabilization from product behavior coverage.

## 5. Non-Goals

- Не начинать новый `/storm:full-cycle`.
- Не менять Gherkin `.feature` wording, acceptance criteria, stories or scenario semantics.
- Не менять server-storage BDD slices.
- Не исправлять Android/iOS `NETSDK1147` blocker.
- Не менять production UI behavior unless the failing order proves a real behavior defect.
- Не делать broad Avalonia test framework rewrite.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Диагностический подход

1. Verify clean baseline:
   - `git status --short`
   - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore`
2. Re-run isolated failing test:
   - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask" --output Detailed`
3. Re-run containing class:
   - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/*" --output Detailed`
4. If class passes, run a smaller suspected state-order window around clipboard/copy/paste/hotkey tests if TUnit filter supports it; otherwise run full suite once.
5. If the failure reproduces only after specific preceding tests, inspect:
   - focus and active tree state in `MainControl`;
   - `vm.GetClipboardTextAsync` / `SetClipboardTextAsync` delegate reset;
   - fixture task cleanup and unique pasted task titles;
   - context menu / selected item / focused text input state;
   - window/session disposal path and `DisposeIgnoringHeadlessTeardownNullReferenceAsync` usage.
6. Fix the smallest proven cause.

### 6.2 Likely fix patterns

Allowed if evidence supports them:
- Strengthen test isolation in `MainControlTreeCommandsUiTests` by resetting delegates or focus/selection state in `finally`.
- Replace implicit timing with existing `WaitFor`/observable condition checks.
- Use explicit selection/focus setup before paste hotkey.
- Use `DisposeIgnoringHeadlessTeardownNullReferenceAsync` if failure involves known headless teardown null-reference path.
- Add narrow regression assertion around the reproduced order if the fix is a test helper/test isolation change.

Allowed only if evidence proves product behavior defect:
- Minimal production change in `MainControl` or related UI command routing.
- Required UI test update/addition for the exact observed behavior.
- STORM report update explaining that this moved from validation stabilization to behavior fix.

Not allowed in this SPEC:
- Broad refactor of `MainControl`.
- Disabling or skipping the failing test.
- Loosening assertions without replacing them with equivalent observable behavior checks.
- Increasing timeouts as the primary fix without evidence.

## 7. Бизнес-правила / Алгоритмы

- Copy/paste outline hotkeys must not execute while text input is focused.
- Paste outline under selected task must create the expected tree under the selected parent after confirmation.
- UI command routing must use the current active/selected tree, not stale shared state from another tab or previous test.
- Test cleanup must not leave shared headless UI state that changes another test's outcome.

## 8. Точки интеграции и триггеры

- Avalonia Headless session lifecycle.
- `MainControl` tree command routing and active tree context.
- `MainWindowViewModelFixture` task repository setup/cleanup.
- Clipboard delegates on `MainWindowViewModel`.
- Notification manager mock confirmation/preview state.

## 9. Изменения модели данных / состояния

- Product data model: no planned changes.
- Test state: may add/reset test-only delegates, helper cleanup or explicit focus/selection setup.
- STORM artifacts: update only validation risk notes if stabilization result changes report status.

## 10. Миграция / Rollout / Rollback

- Rollout: local test-only stabilization or minimal behavior fix if proven.
- Rollback: revert changed test/helper/production file and restore report status to previous validation risk.
- Runtime migration: Не применимо.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
- Failure cause is classified as test isolation, headless environment, order-dependent product behavior, or unresolved environment blocker with evidence.
- Isolated failing test passes.
- `MainControlTreeCommandsUiTests` class-level run passes, or any remaining failure has separate evidence and stop decision.
- A practical full-suite validation run passes, or timeout/failure is documented with exact command, failing test and next SPEC.
- If production UI behavior changes, relevant UI test coverage is added/updated and passes.
- STORM reports are synchronized if validation risk status changes.

Команды проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-24-storm-stabilize-full-suite-ui-state-order.md
```

Full-suite command may be stopped and converted to a separate infrastructure SPEC if it exceeds a practical timebox or exposes a different unrelated failure.

## 12. Риски и edge cases

- Риск: failure is non-deterministic and does not reproduce during EXEC.
  - Смягчение: preserve current known evidence, run class-level and full-suite once, avoid speculative fixes.
- Риск: full suite is too slow for iteration.
  - Смягчение: use class-level and narrowed order windows first; run full suite only after targeted evidence.
- Риск: fix changes product behavior unintentionally.
  - Смягчение: require failing evidence and UI tests before production change.
- Риск: current test relies on shared fixture defaults that another test mutates.
  - Смягчение: inspect cleanup and reset changed delegates/state in `finally`.

## 13. План выполнения

1. Reconfirm clean worktree and read current failing test plus helper lifecycle.
2. Run isolated failing test and class-level `MainControlTreeCommandsUiTests`.
3. If class-level fails, inspect first failing assertion and fix test isolation or behavior based on evidence.
4. If class-level passes, run full suite once and capture exact failure/order.
5. Apply minimal fix only after reproduction evidence.
6. Run targeted/class/full-suite validation as practical.
7. Sync `docs/product/storm.json` and reports if validation risk status changes.
8. Update this SPEC Post-EXEC review and commit if requested.

## 14. Открытые вопросы

Блокирующих вопросов нет. EXEC should begin only after approval phrase.

## 15. Соответствие профилю

- Профиль: QUEST delivery-task + STORM validation follow-up.
- Local AGENTS override respected: UI-facing behavior changes require relevant UI tests.
- Product artifacts remain Russian.
- No code/test changes before SPEC approval.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` | possible narrow test isolation/helper adjustment | Stabilize failing full-suite UI test if evidence supports test-side fix |
| `src/Unlimotion/Views/MainControl.axaml.cs` or related UI command routing file | possible minimal production fix only if proven | Fix real UI command routing behavior if full-suite failure proves product defect |
| `docs/product/storm.json` | possible validation risk sync | Reflect full-suite risk status after stabilization |
| `docs/product/reports/coverage.md` | possible validation risk sync | Keep `/storm:cover` report current |
| `docs/product/reports/ranking.md` | possible next-step sync | Remove or update stabilization recommendation |
| `specs/2026-06-24-storm-stabilize-full-suite-ui-state-order.md` | Post-EXEC evidence | QUEST trace |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Full-suite validation | previous risk: 561/562 with one UI failure; isolated test passed | target: full suite passes or blocker is classified with exact evidence |
| `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` | passed in isolation, failed in full-suite context | target: passes in isolation and in containing class/order context |
| `/storm:cover` reports | validation risk recorded | target: risk resolved or moved to separate blocker with evidence |

## 18. Альтернативы и компромиссы

- Вариант A: начать с Android/iOS `NETSDK1147` environment/setup SPEC.
  - Плюсы: addresses platform evidence blocker.
  - Минусы: does not improve reliability of full test gate for future STORM delivery.
- Вариант B: stabilize full-suite UI state/order first.
  - Плюсы: improves delivery confidence for subsequent `/storm:cover` work; current risk is already documented by recent validation.
  - Минусы: may require slow full-suite reproduction.
- Вариант C: continue adding executable BDD scenarios despite full-suite risk.
  - Плюсы: grows BDD coverage.
  - Минусы: accumulates validation debt after active cover gaps are already closed.
- Выбран Вариант B as next SPEC because full-suite validation quality is the current cross-cutting blocker for trustworthy continuation.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Goal, AS-IS, problem, goals and non-goals are explicit. |
| B. Качество дизайна | 6-10 | PASS | Diagnostic route, allowed fix patterns, integration points and rollback described. |
| C. Безопасность изменений | 11-13 | PASS | Stop rules prevent speculative production changes and broad UI refactor. |
| D. Проверяемость | 14-16 | PASS | Targeted, class-level and full-suite validation commands listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan, alternatives and file scope are concrete. |
| F. Соответствие профилю | 20 | PASS | QUEST gate and UI testing requirements respected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope is one known full-suite UI validation risk. |
| 2. Понимание текущего состояния | 5 | Uses current coverage/ranking reports and failing test context. |
| 3. Конкретность целевого дизайна | 5 | Reproduction-first workflow and allowed fix patterns are explicit. |
| 4. Безопасность | 5 | Production behavior change requires evidence and UI tests. |
| 5. Тестируемость | 5 | Commands cover isolated, class-level, full-suite and artifact validation. |
| 6. Готовность к автономной реализации | 5 | No blocking questions; approval gate remains. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `MainControlTreeCommandsUiTests` failing method, current coverage/ranking reports, local UI testing override.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: based on documented full-suite failure and isolated pass.
  - Contract pass: no code/test changes before approval.
  - Adversarial risk pass: speculative production fixes and test skipping are explicitly blocked.
  - Stop decision: wait for `Спеку подтверждаю`.
- Residual risks: failure may not reproduce; full-suite run may be slow or expose a different unrelated failure.

## Approval

Ожидается фраза: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Проверка состояния после коммита | 0.98 | Нет | Выбрать следующий SPEC-кандидат | Нет | Нет | Worktree clean after `6d56945`; reports show no active cover gaps and one validation stabilization risk. | `git status`, `git log`, `docs/product/reports/*.md` |
| SPEC | Выбор stabilization follow-up | 0.84 | Exact full-suite failure log not retained beyond report summary | Создать SPEC | Нет | Нет | Full-suite reliability is a cross-cutting prerequisite for future STORM delivery confidence. | `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` |
| SPEC | Подготовка SPEC и review | 0.9 | Нет | Запросить подтверждение пользователя | Да | Нет | Changes may touch UI tests or product behavior, so QUEST approval is required. | `specs/2026-06-24-storm-stabilize-full-suite-ui-state-order.md` |
