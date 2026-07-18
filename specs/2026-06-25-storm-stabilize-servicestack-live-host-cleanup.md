# Стабилизация live ServiceStack host cleanup в full-suite

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST SPEC / STORM validation follow-up
- Владелец: Codex + product owner approval gate
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка `storm-bootstrap`
- Instruction stack: central `AGENTS.md` -> `routing-matrix.md` -> `model-behavior-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-ravendb`, `storm-product-development`; local `AGENTS.override.md` applied after central stack
- Ограничения: не менять product behavior, `.feature` wording, acceptance criteria или production ServiceStack/RavenDB contract без отдельного evidence; EXEC только после фразы `Спеку подтверждаю`
- Связанные ссылки: `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`, `src/Unlimotion.Test/ServerStorageCrudRealtimeContract.cs`, `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`, `src/Unlimotion.Test/StormServerStorageCrudRealtimeExecutableSpecTests.cs`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель

Вернуть full `Unlimotion.Test` как надежный validation gate после закрытия UI state/order failure. Текущий full-suite run больше не падает на `MainControlTreeCommandsUiTests`, но завершается process failure после 193 passing tests из-за live ServiceStack host cleanup/file watcher issue.

Outcome contract:
- Success means: причина `ServerContentRoot` / `EventLogInternal` cleanup failure воспроизведена, устранена test-infrastructure изменением или оформлена как внешний environment blocker с точной диагностикой.
- Итоговый output: minimal test infrastructure fix, targeted live tests pass, full-suite passes or stops on a different documented blocker, STORM reports synchronized.
- Stop rules: остановиться, если требуется менять production API semantics, production AppHost/license behavior, Gherkin wording, test annotations unrelated to live integration isolation, or broad test runner policy.

## 2. Текущее состояние (AS-IS)

- `/storm:cover` active behavior gaps закрыты; step-executable scenarios remain 7/45.
- `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` стабилизирован: isolated target прошёл 1/1, `MainControlTreeCommandsUiTests` прошёл 43/43.
- Full-suite command `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed` завершился с exit `-532462766` после 193 passing tests, 0 failed assertions.
- Лог full-suite содержит `Error reading ...\ServerContentRoot\ directory`, затем unobserved task exception и `ObjectDisposedException: EventLogInternal` при попытке ServiceStack логировать ошибку через disposed EventLog logger.
- Targeted `ServerStorageLiveIntegrationTests` проходит 2/2, targeted `StormServerStorageCrudRealtimeExecutableSpecTests` проходит 1/1.

## 3. Проблема

Full-suite gate красный из-за lifecycle cleanup в test-only live ServiceStack host: functional tests pass, но unobserved cleanup exception ломает process exit. Это снижает доверие к `/storm:cover` validation и маскирует будущие реальные регрессии.

## 4. Цели дизайна

- Сначала воспроизвести и локализовать lifecycle boundary: host stop/dispose, file watcher, content root deletion, logger disposal.
- Исправлять test-only infrastructure, не product behavior.
- Сохранить TS-0019/TS-0020/TS-0032 meaning and evidence claims.
- Не расширять ServiceStack production registration, license bootstrap или DTO route behavior.
- Синхронизировать STORM reports только по validation risk, не пересчитывая behavior coverage без новых scenarios.

## 5. Non-Goals

- Не менять production `TaskService` semantics, routes, auth/user-scope behavior или SignalR delivery.
- Не менять `.feature` files, Gherkin scenarios, acceptance criteria или existing test annotations без отдельного evidence.
- Не отключать live tests и не помечать full-suite blocker как skipped.
- Не исправлять Android/iOS `NETSDK1147` blocker.
- Не продолжать `/storm:bdd-implement` новых scenarios в этой SPEC.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Диагностический подход

1. Reconfirm baseline and no stale live hosts:
   - `git status --short`
   - inspect `dotnet`/RavenDB child processes if needed.
2. Re-run targeted live scopes:
   - `ServerStorageLiveIntegrationTests`
   - `StormServerStorageCrudRealtimeExecutableSpecTests`
3. Inspect `ServerStorageCrudRealtimeContract` live host fixture:
   - creation and deletion of `ServerContentRoot`;
   - `WebApplication`/host `StopAsync` and dispose order;
   - RavenDB embedded server disposal;
   - logger provider setup;
   - file watcher or configuration `reloadOnChange` sources.
4. Reproduce a narrower order if possible; otherwise use full-suite evidence.
5. Apply smallest test-infrastructure fix.

### 6.2 Likely fix patterns

Allowed if evidence supports them:
- Stop and dispose Kestrel/ServiceStack host before deleting temp content root.
- Keep temp `ServerContentRoot` alive until all host/file watcher callbacks are quiesced.
- Disable or avoid test-host file watching/reload-on-change for the narrow live host.
- Configure test-host logging providers to avoid Windows EventLog provider in headless tests.
- Add a narrow regression check that runs the affected live contract path without process cleanup exceptions.

Not allowed in this SPEC:
- Suppressing the full test process exit code without fixing the lifecycle cause.
- Removing live ServiceStack/RavenDB evidence from `TS-0020`/`TS-0032`.
- Increasing sleeps/timeouts as primary fix without evidence.

## 7. Бизнес-правила / Алгоритмы

- Server-storage live evidence remains product-relevant only if authenticated task API and SignalR behavior are still verified.
- Test cleanup must not produce unobserved process-level failures after passing assertions.
- Full-suite validation can be called green only when test runner exit code is success, not merely when assertions passed before process crash.

## 8. Точки интеграции и триггеры

- `ServerStorageCrudRealtimeContract.AssertLiveServiceStackTaskApiRoundTripsAuthenticatedUserTasksAsync`.
- `LiveIntegrationTestHost` / narrow `LiveServiceStackTaskApiNarrowAppHost`.
- Temporary content root and RavenDB data directory lifecycle.
- Microsoft.Extensions logging and ServiceStack unobserved task exception handler.
- TUnit full-suite process lifecycle.

## 9. Изменения модели данных / состояния

- Product data model: Не применимо.
- Test state: possible test-only host lifecycle/logging/temp-directory cleanup change.
- STORM state: validation risk notes only.

## 10. Миграция / Rollout / Rollback

- Rollout: test infrastructure change only.
- Rollback: revert changed test helper/fixture files and restore validation risk notes.
- Runtime migration: Не применимо.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
- Root cause classified as test-host cleanup, logger provider cleanup, file watcher cleanup, order-dependent live integration issue, or external environment blocker.
- `ServerStorageLiveIntegrationTests` passes 2/2.
- `StormServerStorageCrudRealtimeExecutableSpecTests` passes 1/1.
- Full `Unlimotion.Test` passes, or stops on a different exact blocker after this cause is removed.
- `storm.json`, `coverage.md` and `ranking.md` reflect final validation status.

Команды проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-25-storm-stabilize-servicestack-live-host-cleanup.md
```

## 12. Риски и edge cases

- Риск: full-suite failure is nondeterministic and targeted scopes pass.
  - Смягчение: preserve exact full-suite evidence and avoid speculative production changes.
- Риск: disabling file watching hides a useful host behavior.
  - Смягчение: apply only to test-only narrow host, not production host.
- Риск: changing logging masks real ServiceStack exceptions.
  - Смягчение: assertions must still verify HTTP error behavior where expected; only prevent disposed logger crash during cleanup.

## 13. План выполнения

1. Confirm clean worktree and no stale live host processes.
2. Read `ServerStorageCrudRealtimeContract` host setup/cleanup.
3. Re-run targeted live scopes and capture current evidence.
4. Inspect and patch the smallest test-only lifecycle/logging boundary.
5. Re-run targeted live scopes.
6. Run full `Unlimotion.Test` once.
7. Sync STORM reports and this SPEC Post-EXEC.
8. Commit if requested.

## 14. Открытые вопросы

Блокирующих вопросов нет. EXEC should begin only after approval phrase.

## 15. Соответствие профилю

- Профиль: QUEST delivery-task + STORM validation follow-up.
- Route: `/storm:cover` continuation with test infrastructure changes, so QUEST approval required.
- Product artifacts remain Russian.
- No code/test changes before SPEC approval.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/ServerStorageCrudRealtimeContract.cs` | possible test-only host cleanup/logging lifecycle fix | Remove full-suite process failure after passing live tests |
| `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs` | possible narrow regression/helper call only if needed | Preserve TS-0019/TS-0020 targeted evidence |
| `src/Unlimotion.Test/StormServerStorageCrudRealtimeExecutableSpecTests.cs` | possible regression/helper call only if needed | Preserve TS-0032 executable BDD evidence |
| `docs/product/storm.json` | validation risk sync | Keep STORM trace current |
| `docs/product/reports/coverage.md` | validation evidence sync | Keep `/storm:cover` report current |
| `docs/product/reports/ranking.md` | next-step sync | Keep recommended next step current |
| `specs/2026-06-25-storm-stabilize-servicestack-live-host-cleanup.md` | Post-EXEC evidence | QUEST trace |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Full-suite validation | process failure after 193 passing tests due to ServiceStack/EventLog cleanup | target: green full-suite or different documented blocker |
| Targeted live evidence | `ServerStorageLiveIntegrationTests` 2/2 and `TS-0032` 1/1 pass | must remain passing |
| Product behavior | unchanged | unchanged |

## 18. Альтернативы и компромиссы

- Вариант A: accept targeted live evidence and ignore full-suite process failure.
  - Плюсы: no extra code.
  - Минусы: full-suite remains unreliable gate.
- Вариант B: stabilize test-only live host cleanup.
  - Плюсы: preserves live evidence and restores full-suite trust.
  - Минусы: requires careful lifecycle debugging.
- Вариант C: remove live ServiceStack evidence from BDD/full-suite.
  - Плюсы: simpler suite.
  - Минусы: weakens AC-0033 evidence and contradicts current STORM trace.
- Выбран Вариант B.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Goal, AS-IS, problem, goals and non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | Diagnostic route and allowed fix patterns scoped to test infrastructure. |
| C. Безопасность изменений | 11-13 | PASS | Stop rules protect production behavior and BDD semantics. |
| D. Проверяемость | 14-16 | PASS | Targeted live and full-suite commands listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan and file scope concrete. |
| F. Соответствие профилю | 20 | PASS | QUEST and STORM route respected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | One root blocker: live host cleanup process failure. |
| 2. Понимание текущего состояния | 5 | Uses current full-suite and targeted evidence. |
| 3. Конкретность целевого дизайна | 5 | Lifecycle/logging/file watcher inspection path explicit. |
| 4. Безопасность | 5 | No production semantics or BDD wording changes. |
| 5. Тестируемость | 5 | Commands cover targeted and full-suite validation. |
| 6. Готовность к автономной реализации | 5 | No blocking questions. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: current full-suite log evidence, targeted live test results, STORM reports and central routing requirements.
- Decision: можно запрашивать подтверждение.
- Stop decision: wait for `Спеку подтверждаю`.
- Residual risks: full-suite may reveal a different blocker after live cleanup is fixed.

## Approval

Получено подтверждение: `Спеку подтверждаю`.

## 20. Post-EXEC Review

- Статус: PASS для scope ServiceStack cleanup.
- Реализовано: `ServerStorageLiveIntegrationFixture` отключает reload-on-change для test host и очищает default logging providers, чтобы cleanup-time file watcher/EventLog provider не ломал process exit после успешных live assertions.
- Product behavior: не менялось.
- `.feature` wording, acceptance criteria и test annotations: не менялись.
- Targeted live evidence:
  - `ServerStorageLiveIntegrationTests` прошло 2/2.
  - `StormServerStorageCrudRealtimeExecutableSpecTests` прошло 1/1.
- Full-suite evidence: `Unlimotion.Test` теперь завершается штатным test summary, прежний `ServerContentRoot` / `EventLogInternal` process crash не воспроизводится.
- Residual blocker: full-suite падает 2 тестами: deterministic `BackupViaGitServiceTests.GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows` и один order-dependent `MainControlResetFiltersUiTests.ResetFiltersButton_IsAvailableOnTaskTabs` Headless dispose failure, который targeted проходит 1/1.
- Decision: отдельная SPEC подготовлена для Windows ACL hardening blocker: `specs/2026-06-26-storm-stabilize-backup-acl-full-suite.md`.

## 21. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего blocker | 0.9 | Нет | Создать SPEC | Нет | Нет | UI state/order failure закрыт targeted/class evidence; full-suite blocker теперь live ServiceStack cleanup. | `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`, full-suite log |
| SPEC | Подготовка SPEC и review | 0.88 | Нет | Запросить подтверждение пользователя | Да | Нет | Fix may touch test infrastructure and full-suite behavior, so QUEST approval is required. | `specs/2026-06-25-storm-stabilize-servicestack-live-host-cleanup.md` |
| EXEC | ServiceStack cleanup fix | 0.86 | Нет | Sync artifacts and prepare next SPEC | Нет | Да | Targeted live tests pass; full-suite no longer crashes on cleanup and now exposes a different ACL blocker. | `src/Unlimotion.Test/ServerStorageCrudRealtimeContract.cs`, `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md` |
