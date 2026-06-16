# STORM cover ST-0014 Telegram callbacks/status/relation/Git timer coverage

## 0. Метаданные
- Тип (профиль): `storm-product-development` + `delivery-task/QUEST`
- Владелец: product/engineering
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка `storm-bootstrap`
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
  - До явного утверждения SPEC не менять production code, tests, test annotations, STORM artifacts и поведение продукта.
  - После утверждения разрешены только изменения, прямо связанные с `ST-0014`, `AC-0040`, `GR-040`, `SC-0014-002`, `CV-0004`.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие артефакты.
  - Не заменять acceptance criteria на Gherkin.
  - Не удалять существующие stories/tests/conflicts/dependencies/reports.
  - Не менять test annotations без отдельного подтверждения.
  - Не использовать реальный Telegram API, bot token, polling loop, внешнюю сеть или реальный Git remote.
  - Не менять публичный callback data format без отдельного product decision.
  - UI tests не обязательны, пока не меняется Avalonia UI behavior; если задача затронет UI, остановиться и расширить SPEC.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/ranking.md`
  - `features/storm/st-0014-telegram-bot.feature`
  - `src/Unlimotion.TelegramBot/Bot.cs`
  - `src/Unlimotion.TelegramBot/TelegramCommandHandler.cs`
  - `src/Unlimotion.TelegramBot/TaskService.cs`
  - `src/Unlimotion.TelegramBot/GitService.cs`
  - `src/Unlimotion.Test/TelegramBotCommandAuthorizationTests.cs`
  - `src/Unlimotion.Test/GitBackupJobTests.cs`

Если пользователь утверждает эту SPEC фразой `Спеку подтверждаю`, это разрешает перейти в EXEC и внести минимальные test/code/artifact changes в рамках этой спеки.

## 1. Overview / Цель
Цель: продолжить `/storm:cover` для `ST-0014` и закрыть или честно сузить gap `CV-0004`: callback-действия Telegram bot, status/relation flows и Git timer behavior из `AC-0040`.

Outcome contract:
- Success means:
  - callback actions получают deterministic automated evidence без реального Telegram API;
  - `SC-0014-002` получает linked test evidence для фактически покрытых callback/status/relation paths;
  - `AC-0040` переводится в `full` только если покрыты callback actions и Git timer part без нового product behavior;
  - если Git timer/conflict-safety требует нового behavior, `AC-0040` остаётся `partial`, а STORM artifacts фиксируют split gap и отдельную `/storm:bdd-implement` recommendation.
- Итоговый артефакт / output:
  - testable callback/user-state seam или более узкий handler, используемый production `Bot`;
  - targeted TUnit tests for Telegram callback/status/relation behavior;
  - timer evidence or explicit blocker/split gap;
  - синхронизированные `storm.json`, feature tags and reports по фактическому evidence;
  - заполненный Post-EXEC review.
- Stop rules:
  - Остановиться, если проверка требует real Telegram API, token, polling, external network или real Git remote.
  - Остановиться, если callback test требует менять публичный callback data format.
  - Остановиться, если Git timer safety отсутствует в текущем TelegramBot behavior и требует feature implementation, а не coverage.
  - Остановиться, если extraction превращается в redesign `Bot.cs` beyond minimal test seam.

## 2. Текущее состояние (AS-IS)
- `CV-0003 / AC-0039 / SC-0014-001` уже покрыт `TS-0022`: allowed-user gate и команды `/start`, `/help`, `/search`, `/task`, `/root`.
- `CV-0004 / AC-0040 / SC-0014-002` остаётся `proposed/draft` без linked tests.
- `Bot.OnCallbackQueryReceived` сейчас обрабатывает callback prefixes:
  - `open_`
  - `status_`
  - `delete_`
  - `createSub_`
  - `createSib_`
  - `parents_`
  - `blocking_`
  - `containing_`
  - `blocked_`
- `Bot.HandleUserState` обрабатывает ввод названия после `createSub_` и `createSib_`.
- `Bot.StartTimers` создаёт `System.Timers.Timer` для `GitService.PullLatestChanges()` и `GitService.CommitAndPushChanges(...)`.
- `Unlimotion.TelegramBot.GitService` не показывает явного conflict-resolution guard; в основном приложении есть отдельные `GitPullJob/GitPushJob` tests, которые проверяют skip при conflict resolution.
- Callback code тесно связан со static `_client`, `_taskService`, `_gitService`, `_userStates` и private methods, поэтому его нельзя надёжно покрыть без seam.

## 3. Проблема
Корневая проблема: `AC-0040` описывает продуктово значимые Telegram callbacks and timer behavior, но текущий код не имеет automated product evidence, а static/private Telegram client coupling мешает безопасной проверке без network side effects.

## 4. Цели дизайна
- Разделение ответственности: отделить callback/user-state decision logic от Telegram transport/polling.
- Повторное использование: production `Bot` должен использовать тот же callback seam, который покрывается тестами.
- Тестируемость: тесты не должны запускать real Telegram polling, Git timers или real Git operations.
- Консистентность: сохранить текущие callback prefixes, русские user-facing ответы и status labels.
- Обратная совместимость: `Bot.StartAsync`, `Program.cs`, current command/auth seam и существующие `TS-0022` не ломать.

## 5. Non-Goals (чего НЕ делаем)
- Не реализуем новое Telegram UX поведение сверх текущего callback contract.
- Не меняем публичный callback data format.
- Не меняем storage schema, task relation model, status model или Git settings schema.
- Не добавляем executable Gherkin runner или step definitions.
- Не меняем test annotations.
- Не добавляем real Telegram integration tests.
- Не меняем Avalonia UI.
- Не исправляем Git conflict-resolution semantics внутри `/storm:cover`, если это окажется новым product behavior.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.TelegramBot/Bot.cs` -> остаётся composition root для Telegram transport, startup, polling и adapter wiring.
- `src/Unlimotion.TelegramBot/TelegramCallbackHandler.cs` или эквивалент -> новый internal handler для callback data, user-state transitions and observable responses.
- `src/Unlimotion.TelegramBot/TelegramCommandHandler.cs` -> не расширять без необходимости; command/auth tests должны остаться зелёными.
- `ITelegramCallbackTaskOperations` или эквивалент -> минимальный task operation interface: get, save/update status, delete, create sub/sibling, relation lists.
- `ITelegramCallbackResponder` или эквивалент -> answer callback, send message, delete message, show task, show task list.
- `ITelegramUserStateStore` или простая in-memory abstraction -> заменить прямую зависимость tests от static `_userStates`.
- Timer seam -> только если current behavior можно проверить без нового behavior; иначе зафиксировать blocker/split gap.
- `src/Unlimotion.Test/TelegramBotCallbackCoverageTests.cs` -> targeted TUnit tests для `AC-0040`.
- `docs/product/storm.json`, `features/storm/st-0014-telegram-bot.feature`, `docs/product/reports/*` -> sync only after actual evidence.

### 6.2 Детальный дизайн
- Callback handler получает:
  - allowed users или общий access gate, совместимый с `TelegramCommandHandler.HasAccess`;
  - callback data, user id, username, chat id, message id;
  - task operation adapter;
  - responder adapter;
  - user state store.
- Covered callback examples:
  - Unauthorized callback не должен читать/изменять tasks и не должен отправлять task data.
  - `open_<id>` показывает выбранную задачу.
  - `status_<status>_<id>` меняет статус задачи, сохраняет её и отвечает текущим status text.
  - `delete_<id>` удаляет задачу, отвечает "Задача удалена" and deletes Telegram message.
  - `createSub_<id>` записывает user state and asks "Введите название подзадачи".
  - `createSib_<id>` записывает user state and asks "Введите название соседней задачи".
  - `parents_`, `blocking_`, `containing_`, `blocked_` показывают соответствующие relation lists or answer empty-state text.
  - Follow-up text after create state creates sub/sibling only if current storage seam supports existing behavior without redesign.
- Timer evidence:
  - Preferred: cover only existing no-real-Git behavior through a small timer scheduler seam if `StartTimers` can be made testable without semantic change.
  - If TelegramBot timers have no conflict-resolution guard and no existing supported conflict state, do not implement guard under this SPEC; update `CV-0004` as split: callbacks covered, timer safety requires separate `/storm:bdd-implement` or product decision.
- Visual planning artifact для UI-facing изменений: `Не применимо`, Telegram bot text/API behavior и background timers не меняют Avalonia UI.
- UI test video evidence для UI automation задач: `Не применимо`; fallback evidence: targeted TUnit output.
- Границы сохранения поведения:
  - permitted: internal handler extraction, adapters, TUnit tests, artifact sync after evidence;
  - forbidden: callback data rename, Telegram.Bot network integration in tests, real Git remote, new conflict policy.
- Обработка ошибок:
  - malformed callback data returns safe callback answer or existing error fallback without crashing;
  - missing task must not throw out of handler;
  - exceptions stay covered by existing `Bot` top-level fallback unless handler can preserve same observable text.
- Производительность:
  - tests remain in-memory and deterministic;
  - no unbounded timers, sleeps or polling in tests.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Only allowed users can execute callback actions.
- Unauthorized callback must not disclose task data.
- Status callback changes task status to one of current `DomainTaskStatus` values.
- Relation callbacks expose only the selected task's relation collections.
- Delete callback removes the selected task and removes the Telegram message.
- Create sub/sibling is a two-step interaction: callback stores user state, next text message supplies the title.
- Git timer behavior can be marked covered only if it is current behavior, not newly invented policy.

## 8. Точки интеграции и триггеры
- `Bot.HandleUpdateAsync` routes `CallbackQuery` to `OnCallbackQueryReceived`.
- `Bot.OnCallbackQueryReceived` should delegate decision logic to the callback handler.
- `Bot.HandleUserState` should delegate create-sub/create-sibling follow-up text if extracted.
- `TaskService.GetTask`, `TaskService.DeleteTask`, `TaskItemViewModel.SaveItemCommand`, relation collections and `TaskStorageInstance` participate through adapter.
- `Bot.StartTimers` and `GitService` are inspected for timer coverage; production timers must not run in tests.
- STORM sync happens after tests pass or after a precise blocker is found.

## 9. Изменения модели данных / состояния
- Production persisted model: не меняется.
- In-memory Telegram user state: may receive a small abstraction; no storage schema change.
- Git repository state: not touched by tests.
- STORM state:
  - add `TS-0023` for callback coverage only after passing targeted evidence;
  - update `SC-0014-002` status according to actual evidence: `passing` if all `AC-0040` covered, otherwise keep partial/split note;
  - do not remove `TS-0022`.

## 10. Миграция / Rollout / Rollback
- Миграция production данных: не применимо.
- Rollout: normal branch change with tests and STORM sync.
- Rollback: revert new handler/tests/artifact sync; `TS-0022` command/auth coverage remains intact.
- Backward compatibility: callback data prefixes, command text, Telegram startup and Git settings remain unchanged.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `AC-0040` receives automated evidence for callback status/relation/create/delete/open paths or a precise split blocker for timer part.
  - Unauthorized callback path does not access task operations.
  - Status callback updates and saves selected task status.
  - Create sub/sibling callback records expected user state and prompts for title; follow-up text is covered if current storage seam supports it safely.
  - Relation callbacks show parents/blocking/containing/blocked lists or empty-state answer.
  - Delete callback deletes selected task and removes the Telegram message.
  - Timer behavior is covered only if current TelegramBot timer contract is testable without adding new conflict policy.
- Какие тесты добавить/изменить:
  - Add `TelegramBotCallbackCoverageTests`.
  - Preserve `TelegramBotCommandAuthorizationTests`.
  - Optionally reuse existing `GitBackupJobTests` as evidence only if STORM story/constraint mapping proves it belongs to `AC-0040`; do not duplicate app-wide Git job tests under TelegramBot without product link.
- Characterization tests / contract checks:
  - callback prefixes remain unchanged;
  - status button labels/status texts remain Russian and consistent with current `DomainTaskStatus`.
- Visual acceptance: `Не применимо`.
- UI video evidence: `Не применимо`.
- Базовые замеры performance: `Не применимо`.
- Команды для проверки:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCallbackCoverageTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCommandAuthorizationTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
- Stop rules для test/retrieval/tool/validation loops:
  - If callback extraction requires public callback data redesign, stop.
  - If create sub/sibling persistence reveals a product bug, stop and propose `/storm:bdd-implement` instead of silently fixing under coverage.
  - If Git timer conflict-safety is absent, do not add it under `/storm:cover`; sync a blocker/split gap.
  - If full suite is too expensive or repeats known timeout behavior, report targeted evidence and residual risk.

## 12. Риски и edge cases
- Риск: `Bot.cs` static state makes extraction larger than intended. Смягчение: keep adapter minimal; stop before broad redesign.
- Риск: create sub/sibling current implementation may not persist parent relation exactly as product wording implies. Смягчение: characterize current behavior first; if mismatch is product bug, stop and create implementation SPEC.
- Риск: relation lists can expose cross-user data if storage already contains it. Смягчение: tests cover only selected task relation behavior; cross-user isolation belongs to storage/server coverage unless Telegram-specific leak is found.
- Риск: `GitService` timers are unconditional while AC wording implies safer configured sync. Смягчение: record split gap instead of adding new behavior.
- Риск: malformed callback data can throw due to `SplitOnFirst` assumptions. Смягчение: handle only if preserving current observable behavior is clear; otherwise record follow-up.
- Риск: `DomainTaskStatus` enum grows. Смягчение: tests should cover current supported statuses or representative status action, not freeze unrelated enum internals unless required by product contract.

## 13. План выполнения
1. Зафиксировать EXEC start: `git status --short`, current `CV-0004` state.
2. Inspect `Bot.OnCallbackQueryReceived`, `HandleUserState`, `TaskService`, `GitService` and current tests.
3. Add minimal callback/user-state handler seam and adapters.
4. Add targeted callback tests for access, open, status, delete, create prompt and relation lists.
5. Decide timer path from evidence:
   - if current timer behavior is testable as-is, add deterministic timer coverage;
   - if not, preserve `AC-0040` partial and create STORM split blocker/recommendation.
6. Run targeted callback tests and command/auth regression tests.
7. Run build.
8. Sync `storm.json`, feature tags and reports according to actual evidence.
9. Run STORM validator and `git diff --check`.
10. Fill Post-EXEC review.

## 14. Открытые вопросы
- Блокирующих вопросов нет до EXEC: approval authorizes a bounded coverage attempt.
- Неблокирующий риск: whether Git timer part can be covered as existing behavior is discovered during EXEC. If it cannot, this SPEC explicitly stops before implementing new behavior.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`.
- Выполненные требования профиля:
  - `/storm:cover` with tests/code/artifact changes goes through QUEST.
  - Existing STORM artifacts are preserved; `/storm:full-cycle` is not restarted.
  - Acceptance criteria are not replaced by Gherkin.
  - Scenario/Test/coverage status changes only after actual evidence.
  - Product artifacts are written in Russian.
  - TUnit validation uses `--treenode-filter`.
  - UI override is accounted for: no UI-facing change planned.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.TelegramBot/Bot.cs` | Delegate callback/user-state logic to testable seam if approved | Remove static/private Telegram API coupling for `AC-0040` coverage |
| `src/Unlimotion.TelegramBot/TelegramCallbackHandler.cs` | New internal callback handler or equivalent | Deterministic callback/status/relation tests |
| `src/Unlimotion.Test/TelegramBotCallbackCoverageTests.cs` | New TUnit tests | Close or narrow `CV-0004` |
| `docs/product/storm.json` | Add `TS-0023` or split blocker, update `SC-0014-002` by evidence | STORM trace sync |
| `features/storm/st-0014-telegram-bot.feature` | Add `@test:TS-0023` only after passing evidence or keep `@draft` with blocker | BDD layer sync |
| `docs/product/reports/bdd-sync.md` | Update Scenario -> Test sync | Reflect actual evidence |
| `docs/product/reports/bdd-lint.md` | Update behavior gap status | Reflect actual evidence |
| `docs/product/reports/coverage.md` | Update `CV-0004`, `AC-0040` and behavior metrics | Continue `/storm:cover` |
| `docs/product/reports/traceability.md` | Add callback trace if passing | Audit chain |
| `docs/product/reports/ranking.md` | Update `CV-0004` status | Keep backlog honest |
| this SPEC | EXEC journal and Post-EXEC review | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `AC-0040` | `partial`, tests отсутствуют | `full` only if callbacks and timer part covered; otherwise partial with split blocker |
| `SC-0014-002` | `draft`, no linked tests | `passing/automated` with `TS-0023` for covered behavior, or draft/partial with explicit timer blocker |
| `CV-0004` | `proposed`, depends on `CV-0003` | `covered`, `partially_covered_callbacks_timer_gap`, or precise blocker by evidence |
| Callback handling | private static path in `Bot.cs` | production path uses testable callback seam |
| Git timers | unconditional static timers in startup | evidence-backed current behavior or separate implementation recommendation |

## 18. Альтернативы и компромиссы
- Вариант: Reflection tests against private static `Bot` methods.
  - Плюсы: fewer production edits.
  - Минусы: brittle, hard to isolate `_client`, `_taskService`, `_userStates`, timers.
  - Почему не выбран: does not create maintainable product evidence for callbacks.
- Вариант: Real Telegram integration test.
  - Плюсы: high end-to-end fidelity.
  - Минусы: needs token/network/polling, flaky and unsafe for local CI.
  - Почему не выбран: violates constraints.
- Вариант: Implement missing Git conflict guard now.
  - Плюсы: could satisfy timer wording.
  - Минусы: new product behavior, not pure coverage.
  - Почему не выбран сейчас: `/storm:cover` should not silently implement missing behavior; use `/storm:bdd-implement` if needed.
- Вариант: Skip `CV-0004` and move to `CV-0005`.
  - Плюсы: avoids callback/timer complexity.
  - Минусы: leaves only remaining ST-0014 scenario draft.
  - Почему не выбран: ranking recommends `CV-0004` after `CV-0003`.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals and non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | Callback seam, timer decision path, data/state and rollback described. |
| C. Безопасность изменений | 11-13 | PASS | Real Telegram/Git side effects and hidden behavior changes forbidden. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, tests and commands listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, alternatives and bounded plan support EXEC. |
| F. Соответствие профилю | 20 | PASS | STORM + QUEST + TUnit rules covered. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope limited to `CV-0004 / AC-0040 / SC-0014-002`. |
| 2. Понимание текущего состояния | 5 | Captures existing command coverage, callback prefixes, static coupling and timer uncertainty. |
| 3. Конкретность целевого дизайна | 5 | Handler/adapters, test cases and artifact sync rules are explicit. |
| 4. Безопасность (миграция, откат) | 5 | No schema/API callback format/Git remote changes; rollback is simple. |
| 5. Тестируемость | 5 | Targeted TUnit and validation commands are concrete. |
| 6. Готовность к автономной реализации | 5 | EXEC can proceed with clear stop rules for timer or product bugs. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-storm-cover-telegram-callbacks-git-timers.md`, central stack, local override, `storm-product-development`, `docs/product/storm.json`, coverage/ranking reports, `features/storm/st-0014-telegram-bot.feature`, `Bot.cs`, `TaskService.cs`, `GitService.cs`, `TelegramCommandHandler.cs`, `TelegramBotCommandAuthorizationTests.cs`, `GitBackupJobTests.cs`.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: PASS, planned files and evidence sources are listed.
  - Contract pass: PASS, no acceptance criteria replacement, no real Telegram/Git side effects, no test annotations.
  - Adversarial risk pass: PASS, create relation persistence and timer conflict-safety risks are explicit stop/split conditions.
  - Re-review after fixes / Fix and re-review: PASS, no blocking rewrite required.
  - Stop decision: PASS, wait for `Спеку подтверждаю`.
- Evidence inspected:
  - `SC-0014-002` is currently `draft`, linked to `AC-0040`, no linked tests.
  - `CV-0004` ranking depends on completed `CV-0003`.
  - `Bot.OnCallbackQueryReceived` contains callback logic for open/status/delete/create/relation actions.
  - `Bot.StartTimers` starts unconditional pull/push timers against `GitService`.
  - Existing `GitBackupJobTests` prove app-wide conflict guard for `GitPullJob/GitPushJob`, but not TelegramBot timers.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, no ST-0011/CV-0005/CV-0007/UI scope.
  - Acceptance criteria: PASS, `AC-0040` is the only target.
  - Validation evidence: PASS, commands and artifact validator listed.
  - Unsupported claims: PASS, spec does not promise full `AC-0040` if timer behavior is missing.
  - Regression / edge case: PASS, malformed callbacks, unauthorized access, relation lists and timer side effects considered.
  - Comments/docs/changelog: PASS, changelog not required; product artifacts remain Russian.
  - Hidden contract change: PASS, callback data format and new Git policy are forbidden.
  - Manual-review challenge: reviewer may ask whether timer part is over-scoped; spec handles it by splitting blocker instead of implementing new behavior under coverage.
- No-findings justification: SPEC is bounded, has explicit evidence rules, and stops before product behavior implementation.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | feasibility | Timer part of `AC-0040` may not be coverable as existing TelegramBot behavior. | During EXEC, either prove existing behavior or leave split gap and propose `/storm:bdd-implement`. | accepted-risk |

- Fixed before continuing: не применимо.
- Checks rerun: central stack docs, current STORM artifacts/reports and relevant Telegram/Git test/code files inspected.
- Needs human: требуется `Спеку подтверждаю`.
- Residual risks / follow-ups: if timer safety is product-required but absent, next step becomes separate implementation SPEC, not hidden coverage work.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, callback seam diff, targeted test output, STORM artifacts/reports, feature file, validator output.
- Decision: можно завершать EXEC; `CV-0004` закрыт частично по callback subset, timer part оставлен отдельным implementation gap.
- Review passes:
  - Scope/Evidence pass: PASS, изменения ограничены `ST-0014/AC-0040/CV-0004`, callback handler/tests and STORM sync.
  - Contract pass: PASS, callback data format preserved; no real Telegram API, token, polling, network, real Git remote or test annotations.
  - Adversarial risk pass: PASS, timer/conflict-safety is not claimed as covered because current `TelegramBot.StartTimers` has no conflict-resolution guard.
  - Re-review after fixes / Fix and re-review: PASS, fixed one artifact validator warning for scenario `coverage_role`; validator rerun clean.
  - Stop decision: PASS, finish with residual timer gap.
- Evidence inspected:
  - `TelegramBotCallbackCoverageTests` passed 7/7.
  - `TelegramBotCommandAuthorizationTests` passed 7/7.
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore` passed with existing warnings.
  - `validate-artifacts.py docs\product\storm.json` returned OK, 0 errors, 0 warnings.
  - `SC-0014-003` linked to `TS-0023`; `SC-0014-002` remains draft with `@gap:git-timers`.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, no ST-0011/CV-0005/CV-0007/UI behavior edits.
  - Acceptance criteria: PASS, `AC-0040` remains partial because callbacks are covered but timer behavior is not.
  - Validation evidence: PASS, build, two targeted TUnit classes and STORM validator completed.
  - Unsupported claims: PASS, no claim that Git timer/conflict-safety is covered.
  - Regression / edge case: PASS, unauthorized callback, invalid status, relation empty state and command/auth regression covered.
  - Comments/docs/changelog: PASS, changelog not required; product artifacts in Russian.
  - Hidden contract change: PASS, callback prefixes preserved and production callback path delegates to tested handler.
  - Manual-review challenge: reviewer may ask why `AC-0040` remains partial after `TS-0023`; answer: `AC-0040` includes Git timer behavior absent from current TelegramBot guard, so covering it would require new product behavior.
- No-findings justification: callback scope has passing evidence and timer gap is explicit rather than hidden.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | follow-up | Git timer/conflict-safety part of `AC-0040` remains uncovered. | Create separate `/storm:bdd-implement ST-0014` SPEC if TelegramBot timers are product-supported. | follow-up |

- Fixed before final report: artifact `coverage_role` warning fixed; new nullable warning in callback test avoided.
- Checks rerun: build, `TelegramBotCallbackCoverageTests`, `TelegramBotCommandAuthorizationTests`, STORM validator.
- Validation evidence: targeted Telegram callback tests 7/7; command/auth regression 7/7; STORM validator OK 0/0.
- Unrelated changes: none observed before EXEC besides this approved SPEC; current changes are in approved scope.
- Needs human: choose whether timer gap becomes `/storm:bdd-implement ST-0014` or next `/storm:cover` goes to `CV-0005` after platform policy.
- Residual risks / follow-ups: full suite not run; scoped Telegram evidence is green.

## Approval
Утверждено пользователем фразой: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создать follow-up SPEC для `CV-0004` | 0.84 | EXEC покажет, coverable ли timer part как existing behavior | Запросить `Спеку подтверждаю` | Да | Да, пользователь сказал "продолжай", что interpreted as request to prepare the next SPEC, not EXEC approval phrase | После закрытия `CV-0003` ranking recommends `CV-0004`; tests/code changes require QUEST gate. | `specs/2026-06-16-storm-cover-telegram-callbacks-git-timers.md` |
| EXEC | Вынести callback seam и покрыть Telegram callbacks | 0.88 | Timer guard не подтверждён текущим кодом | Синхронизировать STORM artifacts | Нет | Да, пользователь подтвердил SPEC | `TelegramCallbackHandler` сохраняет callback data contract и позволяет проверить unauthorized/open/status/delete/create/relation paths без Telegram API. | `src/Unlimotion.TelegramBot/TelegramCallbackHandler.cs`, `src/Unlimotion.TelegramBot/Bot.cs`, `src/Unlimotion.Test/TelegramBotCallbackCoverageTests.cs` |
| EXEC | Синхронизировать STORM artifacts and reports | 0.86 | Нужен product decision по timer gap | Финальная валидация и отчёт | Да | Нет, next choice pending | `TS-0023` связан с новым `SC-0014-003`; `SC-0014-002` оставлен draft с `@gap:git-timers`. | `docs/product/storm.json`, `features/storm/st-0014-telegram-bot.feature`, `docs/product/reports/*`, this SPEC |
