# STORM BDD Implement ST-0014: Telegram Git Timer Conflict-Safety

## 0. Метаданные
- Тип (профиль): `storm-product-development` + `delivery-task/QUEST`.
- Владелец: product/engineering.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая ветка `storm-bootstrap`.
- Instruction stack:
  - `C:\Users\Kibnet\.codex\agents\AGENTS.md`
  - `instructions/governance/routing-matrix.md`
  - `instructions/core/model-behavior-baseline.md`
  - `instructions/core/quest-governance.md`
  - `instructions/core/quest-mode.md`
  - `instructions/core/testing-baseline.md`
  - `instructions/contexts/testing-dotnet.md`
  - `instructions/profiles/storm-product-development.md`
  - `instructions/governance/spec-linter.md`
  - `instructions/governance/spec-rubric.md`
  - `instructions/governance/review-loops.md`
  - локальный `AGENTS.override.md`
- Ограничения:
  - До явного утверждения SPEC не менять production code, tests, test annotations, STORM artifacts, `.feature` files и поведение продукта.
  - После утверждения менять только `ST-0014 / AC-0040 / SC-0014-002 / CV-0004` и напрямую связанные TelegramBot Git timer code/tests/artifacts.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие артефакты.
  - Не заменять acceptance criteria на Gherkin.
  - Не менять test annotations без отдельного подтверждения.
  - Не использовать реальный Telegram API, bot token, polling loop, внешнюю сеть или real Git remote.
  - Не менять публичный callback data format.
  - Не менять Avalonia UI; если неожиданно потребуется UI behavior change, остановиться и расширить SPEC with UI tests.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/ranking.md`
  - `docs/product/reports/bdd-sync.md`
  - `features/storm/st-0014-telegram-bot.feature`
  - `src/Unlimotion.TelegramBot/Bot.cs`
  - `src/Unlimotion.TelegramBot/GitService.cs`
  - `src/Unlimotion.TelegramBot/GitSettings.cs`
  - `src/Unlimotion.Test/TelegramBotCallbackCoverageTests.cs`
  - `src/Unlimotion.Test/TelegramBotCommandAuthorizationTests.cs`
  - `src/Unlimotion.Test/GitBackupJobTests.cs`
  - `src/Unlimotion/Scheduling/Jobs/GitPullJob.cs`
  - `src/Unlimotion/Scheduling/Jobs/GitPushJob.cs`

## 1. Overview / Цель
Реализовать недостающий BDD/SDD шаг для `ST-0014`: TelegramBot Git timers не должны выполнять pull/push, когда в локальном Git backup уже идёт conflict resolution.

Outcome contract:
- Success means:
  - `SC-0014-002` получает automated/passing evidence для Git timer conflict-safety без real Telegram/Git remote side effects;
  - `AC-0040` перестает быть partial из-за timer gap, если callbacks already covered and timer guard passes;
  - production timer path uses the same guard behavior as tests;
  - STORM artifacts, feature file and reports are synchronized after evidence.
- Итоговый артефакт / output:
  - minimal testable Telegram Git timer seam;
  - targeted TUnit tests for conflict-in-progress skip and no-conflict run;
  - preserved callback/command tests;
  - synchronized `storm.json`, `features/storm/st-0014-telegram-bot.feature` and `docs/product/reports/*`;
  - Post-EXEC review with validation evidence.
- Stop rules:
  - Остановиться, если implementation требует real Telegram polling/token/network or real Git remote.
  - Остановиться, если guard cannot be implemented without broad redesign of Git backup architecture.
  - Остановиться, если current TelegramBot Git settings are incompatible with any conflict-status source; propose product/system-design SPEC.
  - Остановиться, если feature would require UI behavior changes.

## 2. Текущее состояние (AS-IS)
- `ST-0014` уже имеет:
  - `TS-0022`: command/auth coverage passed 7/7.
  - `TS-0023`: callback open/status/delete/create/relation coverage passed 7/7.
- `AC-0040` остается partial because timer/conflict-safety part is missing.
- `SC-0014-002` remains draft with `@gap:git-timers`.
- `Bot.StartTimers` currently creates `System.Timers.Timer` instances and directly calls:
  - `_gitService.PullLatestChanges()`;
  - `_gitService.CommitAndPushChanges("Автоматический коммит изменений.")`.
- `Unlimotion.TelegramBot.GitService` catches/logs Git errors but does not expose a conflict-resolution guard.
- Main app scheduler already has the behavior pattern:
  - `GitPullJob.Execute` skips pull when `IRemoteBackupService.GetConflictStatus().IsInProgress == true`;
  - `GitPushJob.Execute` skips push under the same condition;
  - `GitBackupJobTests` covers skip/run paths.
- TelegramBot project references `Unlimotion`, so it can reuse contracts from the main app where appropriate, but a broad migration to `IRemoteBackupService` is not required for this task.

## 3. Проблема
Одна корневая проблема: TelegramBot Git timers perform Git pull/push unconditionally, so `AC-0040` cannot be marked covered while conflict resolution may be in progress.

## 4. Цели дизайна
- Разделение ответственности: timer scheduling отдельно от Git operation decision.
- Повторное использование: follow the existing main-app rule "skip Git sync during conflict resolution".
- Тестируемость: tests trigger timer actions synchronously without real timers, Telegram polling or Git remote.
- Консистентность: keep existing timer intervals, Git settings, log messages and commit message unless a testable seam requires minor internal restructuring.
- Обратная совместимость: no callback/command behavior changes.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем callback actions, command actions, Telegram access rules or public callback data.
- Не меняем file storage/task schema/Git settings schema.
- Не вводим executable Gherkin runner or step definitions.
- Не добавляем integration tests with real Telegram API, LibGit2 remote or network.
- Не объединяем полностью TelegramBot `GitService` with desktop `BackupViaGitService`.
- Не решаем platform runtime validation for Android/browser/iOS.
- Не возвращаем `CV-0007` attachment workflow в active cover queue.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.TelegramBot/Bot.cs` -> keeps startup/composition and timer creation; delegates timer elapsed behavior to a small testable component.
- `src/Unlimotion.TelegramBot/GitService.cs` -> remains concrete Git adapter; may implement a narrow interface for pull/push/conflict-status checks.
- New `TelegramGitTimerHandler` or equivalent -> decides whether pull/push should run based on conflict status and invokes Git operations.
- New narrow interfaces:
  - `ITelegramGitSyncOperations`: `PullLatestChanges()`, `CommitAndPushChanges(string message)`, `IsConflictResolutionInProgress()`.
  - Optional `ITelegramTimerScheduler` only if needed to test `StartTimers` without `System.Timers.Timer`.
- `src/Unlimotion.Test/TelegramBotGitTimerConflictSafetyTests.cs` -> deterministic TUnit tests for skip/run paths.
- STORM artifacts and feature file -> updated only after tests pass.

### 6.2 Детальный дизайн
- Production path:
  1. `Bot.StartAsync` creates existing `_gitService`.
  2. `StartTimers` creates a handler using `_gitService`.
  3. Pull timer elapsed calls handler pull method.
  4. Push timer elapsed calls handler push method.
  5. Handler checks `IsConflictResolutionInProgress()` before Git operations.
- Conflict-status source:
  - Preferred minimal implementation: `GitService.IsConflictResolutionInProgress()` inspects the configured repository for active merge/conflict-resolution state without mutating repo.
  - If there is already a safe reusable main-app helper for conflict detection, use that helper rather than duplicating ad hoc logic.
  - If repository is invalid/unavailable, keep existing logging/error behavior and do not silently claim conflict-safety beyond testable states.
- Test cases:
  - pull timer skips pull when conflict resolution is in progress;
  - push timer skips push when conflict resolution is in progress;
  - pull/push run when no conflict is in progress;
  - conflict status is checked before each operation;
  - existing `TelegramBotCallbackCoverageTests` and `TelegramBotCommandAuthorizationTests` remain green.
- Gherkin update:
  - `SC-0014-002` can move from `draft` to `passing` only after `TS-0025` or equivalent test evidence exists.
  - `SC-0014-003` remains linked to callback subset `TS-0023`.
- Visual planning artifact: `Не применимо`; no Avalonia UI change.
- UI test video evidence: `Не применимо`; fallback evidence is targeted TUnit output.
- Error handling:
  - Handler logs skip/run events but does not throw timer exceptions outward.
  - Existing `GitService` catch/log style remains unless a testable interface requires return values.
- Performance:
  - Conflict-status check must be lightweight and performed only on timer elapsed, not in a tight loop.

## 7. Бизнес-правила / Алгоритмы
- TelegramBot Git pull timer must not pull while conflict resolution is in progress.
- TelegramBot Git push timer must not commit/push while conflict resolution is in progress.
- When no conflict resolution is in progress, timers continue their existing pull/push behavior.
- Timer tests must not use real time waiting; they call handler methods directly.
- Git conflict-safety is product behavior only for `ST-0014/AC-0040`; it does not change desktop scheduler semantics.

## 8. Точки интеграции и триггеры
- `Bot.StartTimers` timer elapsed callbacks.
- `GitService.PullLatestChanges`.
- `GitService.CommitAndPushChanges`.
- New conflict-status method or adapter.
- Existing STORM chain:
  - Story: `ST-0014`
  - AC: `AC-0040`
  - Rule: `GR-040`
  - Scenario: `SC-0014-002`
  - Existing callback scenario: `SC-0014-003`

## 9. Изменения модели данных / состояния
- Persisted task data: no changes.
- Git config schema: no changes.
- Telegram user state: no changes.
- STORM state:
  - add a new test link, likely `TS-0025`;
  - update `SC-0014-002` from `draft` to `passing` if validation passes;
  - update `AC-0040` coverage from partial to critical/full only if timer evidence closes the remaining gap;
  - preserve `TS-0022` and `TS-0023`.

## 10. Миграция / Rollout / Rollback
- Data migration: not applicable.
- Rollout: normal code/test/artifact change after approved SPEC.
- Rollback: revert timer handler/tests/artifact sync; callback and command coverage remains intact.
- Backward compatibility: timer intervals and commit message stay the same.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. Pull timer path checks conflict state and skips pull while conflict resolution is in progress.
  2. Push timer path checks conflict state and skips commit/push while conflict resolution is in progress.
  3. Pull and push still run when conflict state is clear.
  4. Tests do not use real Telegram API, polling, network, real timers or real Git remote.
  5. Existing `TelegramBotCallbackCoverageTests` and `TelegramBotCommandAuthorizationTests` still pass.
  6. STORM validator passes after artifact sync.
- Какие тесты добавить/изменить:
  - Add `TelegramBotGitTimerConflictSafetyTests` or equivalent.
  - Keep existing Telegram callback/command tests unchanged unless a compile-safe interface adjustment is necessary.
- Characterization tests / contract checks:
  - existing main-app `GitBackupJobTests` remain as reference evidence but are not enough for TelegramBot timer coverage.
- Visual acceptance: `Не применимо`.
- UI video evidence: `Не применимо`.
- Commands for validation:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotGitTimerConflictSafetyTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCallbackCoverageTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCommandAuthorizationTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - `rg -n "[ \t]+$" docs/product features/storm specs/2026-06-18-storm-bdd-implement-telegram-git-timer-conflict-safety.md src/Unlimotion.TelegramBot src/Unlimotion.Test`
- Stop rules:
  - If build requires package restore, try approved local build path only; do not repair workload/restore environment beyond scope.
  - If conflict detection cannot be implemented without real repository mutation, stop and report blocker.
  - If implementation requires broad Git service redesign, stop and propose a system-design SPEC.

## 12. Риски и edge cases
- Conflict detection could differ between LibGit2 repository state and main-app `BackupConflictStatus`. Mitigation: reuse existing helper if available or test only unambiguous conflict/no-conflict adapter behavior.
- `System.Timers.Timer` exceptions may be swallowed/logged. Mitigation: handler tests are synchronous; production timer wrapper keeps catch/log behavior if needed.
- GitService currently catches exceptions and returns void, making outcome observation hard. Mitigation: tests target handler decisions with fake operations, not LibGit2 side effects.
- Moving too much Git logic into TelegramBot would duplicate desktop backup architecture. Mitigation: narrow interface and minimal handler only.
- If `SC-0014-002` wording remains too broad after timer fix, split/clarify scenario text rather than claiming unrelated file storage behavior.

## 13. План выполнения
1. Confirm approved SPEC and clean worktree.
2. Add minimal timer handler/interface around TelegramBot Git timer actions.
3. Adapt `Bot.StartTimers` to call handler methods from timer elapsed callbacks.
4. Add targeted TUnit tests with fake Git operations/conflict state.
5. Run targeted timer tests.
6. Run Telegram callback and command regression tests.
7. Run build.
8. Sync `storm.json`, `features/storm/st-0014-telegram-bot.feature` and reports based on actual evidence.
9. Run STORM validator and hygiene checks.
10. Fill Post-EXEC review.

## 14. Открытые вопросы
- Блокирующих вопросов нет до EXEC: user phrase `Спеку подтверждаю` authorizes implementing the timer conflict-safety behavior described here.
- If product owner does not want TelegramBot timers to be supported behavior, do not approve this SPEC; use an artifact decision SPEC to de-scope `SC-0014-002` instead.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`.
- Выполненные требования профиля:
  - `/storm:bdd-implement ST-0014` goes through delivery-task/QUEST.
  - Gherkin scenario/test/artifact updates happen after test evidence.
  - Product artifacts remain Russian.
  - TUnit validation uses `--treenode-filter`.
  - No UI behavior planned, local UI override does not require UI tests for this scope.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-18-storm-bdd-implement-telegram-git-timer-conflict-safety.md` | New SPEC and EXEC journal | QUEST audit trail |
| `src/Unlimotion.TelegramBot/Bot.cs` | Delegate timer elapsed behavior to testable handler | Production timer path must use tested guard |
| `src/Unlimotion.TelegramBot/GitService.cs` | Add narrow conflict-status operation/interface if needed | Expose safe conflict state to timer handler |
| `src/Unlimotion.TelegramBot/TelegramGitTimerHandler.cs` | New handler or equivalent | Deterministic timer conflict-safety behavior |
| `src/Unlimotion.Test/TelegramBotGitTimerConflictSafetyTests.cs` | New TUnit tests | Automated evidence for `SC-0014-002` |
| `docs/product/storm.json` | Update `ST-0014/AC-0040/SC-0014-002/TS-0025` after evidence | STORM canonical sync |
| `features/storm/st-0014-telegram-bot.feature` | Update tags/status for `SC-0014-002` after evidence | BDD layer sync |
| `docs/product/reports/*` | Update coverage/ranking/traceability/bdd reports | Human-readable sync |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0014-002` | `draft`, `@gap:git-timers`, no test link | `passing/automated` with timer conflict-safety test if validation passes |
| `AC-0040` | partial because timer gap remains | critical/full if callback and timer evidence cover the behavior |
| Telegram timers | direct calls to `_gitService` | guarded handler checks conflict state before Git operations |
| Tests | callbacks and commands covered | timer conflict-safety covered too |

## 18. Альтернативы и компромиссы
- Вариант: de-scope TelegramBot timers from product behavior.
  - Плюсы: no code change.
  - Минусы: leaves AC wording and scenario gap misleading.
  - Почему не выбран: user asked to move forward after coverage work; current ranking treats this as remaining behavior gap.
- Вариант: reuse desktop `GitPullJob/GitPushJob` directly inside TelegramBot.
  - Плюсы: strongest reuse.
  - Минусы: may require larger dependency/config/service wiring change.
  - Почему не chosen by default: start with a narrow handler; reuse helpers only if cheap and clean.
- Вариант: test `Bot.StartTimers` with real `System.Timers.Timer`.
  - Плюсы: closer to runtime.
  - Минусы: flaky, slow, timing-dependent.
  - Почему не chosen: handler-level tests are deterministic and prove the behavior.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals and non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | Handler, conflict rule, integration, data and rollback boundaries described. |
| C. Безопасность изменений | 11-13 | PASS | Real Telegram/Git side effects and broad redesign forbidden. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, test plan and commands listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, alternatives and bounded plan support EXEC after approval. |
| F. Соответствие профилю | 20 | PASS | STORM + QUEST + TUnit requirements covered. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope limited to Telegram Git timer conflict-safety for `ST-0014/AC-0040`. |
| 2. Понимание текущего состояния | 5 | Captures callbacks already covered, timer gap and existing desktop scheduler guard. |
| 3. Конкретность целевого дизайна | 5 | Handler/interface/test strategy and artifact sync rules are explicit. |
| 4. Безопасность (миграция, откат) | 5 | No schema/network/real timer side effects; rollback is bounded. |
| 5. Тестируемость | 5 | Targeted TUnit commands and regression tests are concrete. |
| 6. Готовность к автономной реализации | 5 | EXEC can proceed with clear stop rules. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: this SPEC, current `ranking.md`, `coverage.md`, `features/storm/st-0014-telegram-bot.feature`, `Bot.cs`, `GitService.cs`, `GitSettings.cs`, `GitBackupJobTests.cs`, `GitPullJob.cs`, `GitPushJob.cs`, `TelegramBotCallbackCoverageTests.cs`.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: reviewed current committed state after Вариант B and confirmed only remaining active behavior gap is `ST-0014/AC-0040` timer conflict-safety.
  - Contract pass: code/test changes are gated until approval; no test annotations, UI behavior, real Telegram/Git operations or full-cycle restart.
  - Adversarial risk pass: broad Git architecture reuse could explode scope; SPEC requires a narrow handler and stop rule for broad redesign.
  - Re-review after fixes / Fix and re-review: no blocking fixes required.
  - Stop decision: PASS.
- Evidence inspected:
  - `Bot.StartTimers` directly calls `_gitService.PullLatestChanges()` and `_gitService.CommitAndPushChanges(...)`.
  - `GitService` has no conflict guard.
  - `GitPullJob/GitPushJob` skip when `GetConflictStatus().IsInProgress`.
  - `GitBackupJobTests` already prove the main-app skip pattern.
  - `SC-0014-002` is draft and `SC-0014-003` already covers callbacks.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, excludes CV-0007, ST-0015, UI and attachment workflow.
  - Acceptance criteria: PASS, tied to pull/push skip/run behavior.
  - Validation evidence: PASS, commands listed.
  - Unsupported claims: PASS, `AC-0040` only becomes full/critical after tests.
  - Regression / edge case: PASS, no-conflict run path and conflict skip path both included.
  - Comments/docs/changelog: PASS, no changelog required; product artifacts will stay Russian.
  - Hidden contract change: PASS, callback/user command contracts untouched.
  - Manual-review challenge: reviewer may ask why Telegram should reuse main-app conflict semantics; AC already mentions Git timers and ranking identifies timer conflict-safety as the gap.
- No-findings justification: SPEC is bounded, testable and aligns with current remaining STORM gap.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | implementation-risk | Exact conflict-status source in TelegramBot may need a narrow adapter rather than direct main-app service reuse. | Prefer minimal handler/interface; stop if broad redesign is required. | accepted-risk |

- Fixed before continuing: не применимо.
- Checks rerun: Manual SPEC review and hygiene checks before final response.
- Needs human: `Спеку подтверждаю`.
- Residual risks / follow-ups: If this behavior is not product-supported, do not approve this SPEC; de-scope `SC-0014-002` artifact-only instead.

### Post-EXEC Review
- Статус: Не выполнен до EXEC
- Scope reviewed: Не применимо до approval.
- Decision: Не применимо.
- Review passes:
  - Scope/Evidence pass: Не применимо.
  - Contract pass: Не применимо.
  - Adversarial risk pass: Не применимо.
  - Re-review after fixes / Fix and re-review: Не применимо.
  - Stop decision: Не применимо.
- Evidence inspected: Не применимо.
- Depth checklist:
  - Scope drift / unrelated changes: Не применимо.
  - Acceptance criteria: Не применимо.
  - Validation evidence: Не применимо.
  - Unsupported claims: Не применимо.
  - Regression / edge case: Не применимо.
  - Comments/docs/changelog: Не применимо.
  - Hidden contract change: Не применимо.
  - Manual-review challenge: Не применимо.
- No-findings justification: Не применимо до EXEC.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | execution | EXEC еще не выполнялся. | Запустить только после фразы `Спеку подтверждаю`. | pending |

- Fixed before final report: Не применимо.
- Checks rerun: Не применимо.
- Validation evidence: Не применимо.
- Unrelated changes: Не применимо.
- Needs human: approval.
- Residual risks / follow-ups: Не применимо.

## Approval
Ожидается фраза: `Спеку подтверждаю`

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор evidence для timer gap | 0.9 | Approval | Создать SPEC | Нет | Нет | Ranking после Варианта B оставляет `ST-0014/AC-0040` timer conflict-safety единственным active behavior gap. | `docs/product/reports/*`, `features/storm/st-0014-telegram-bot.feature`, Telegram/Git code/tests |
| SPEC | Post-SPEC review | 0.91 | Approval | Передать SPEC пользователю | Да | Нет | SPEC bounded to timer conflict-safety and blocks code/tests until approval. | `specs/2026-06-18-storm-bdd-implement-telegram-git-timer-conflict-safety.md` |
