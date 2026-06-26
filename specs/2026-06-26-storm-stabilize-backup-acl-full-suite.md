# Стабилизация Windows ACL hardening test в full-suite

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST SPEC / STORM validation follow-up
- Владелец: Codex + product owner approval gate
- Масштаб: small/medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка `storm-bootstrap`
- Instruction stack: central `AGENTS.md` -> `routing-matrix.md` -> `model-behavior-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `storm-product-development`; local `AGENTS.override.md` applied after central stack
- Ограничения: не менять product behavior backup/credentials flow без отдельного evidence; EXEC только после фразы `Спеку подтверждаю`
- Связанные ссылки: `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель

Вернуть full `Unlimotion.Test` как надежный validation gate после закрытия live ServiceStack cleanup process failure. Текущий full-suite run завершается штатно, но падает на `BackupViaGitServiceTests.GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows`.

Outcome contract:
- Success means: Windows ACL assertion classified and fixed narrowly, or оформлен как environment blocker с точной диагностикой.
- Итоговый output: minimal test or service fix, targeted ACL test pass, full-suite pass or stop on a different documented blocker, STORM reports synchronized.
- Stop rules: остановиться, если требуется менять credential lookup semantics, Git transport behavior, SSH key paths, production backup scheduling or unrelated UI tests.

## 2. Текущее состояние (AS-IS)

- `ServerStorageLiveIntegrationTests` проходит 2/2.
- `StormServerStorageCrudRealtimeExecutableSpecTests` проходит 1/1.
- Full-suite command `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed` больше не падает process crash, а завершается обычным test failure summary: 563 total, 561 passed, 2 failed.
- Deterministic blocker: targeted `BackupViaGitServiceTests.GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows` падает 1/1 на ожидании, что `HasAccessRule(privateKeyPath, BuiltinUsers, includeInherited: true)` станет `false`.
- Secondary risk: `MainControlResetFiltersUiTests.ResetFiltersButton_IsAvailableOnTaskTabs` один раз упал в full-suite на Avalonia Headless dispose, но targeted rerun прошел 1/1.

## 3. Проблема

ACL test проверяет hardening private SSH key permissions, но текущая assertion смешивает explicit rule removal and inherited parent permissions. На Windows runner/workspace файл может сохранять inherited `BUILTIN\Users` access даже после удаления explicit rule, поэтому test fails без доказанного product regression.

## 4. Цели дизайна

- Разделить explicit ACL hardening и inherited ACL environment behavior.
- Сохранить security intent: private key не должен оставаться с лишним explicit read rule после `GetCredentials`.
- Не ослаблять production hardening без evidence.
- Full-suite validation считать зеленым только по успешному process exit.
- Если Headless dispose failure повторится targeted, оформить отдельный UI stabilization scope.

## 5. Non-Goals

- Не менять SSH credential selection, Git fetch/push behavior или backup scheduling.
- Не менять unrelated backup tests.
- Не менять Avalonia UI tests в этой SPEC, если Headless dispose не воспроизводится отдельно.
- Не трогать STORM Gherkin scenarios, acceptance criteria или test annotations без отдельного evidence.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Диагностический подход

1. Re-run targeted ACL test with detailed output.
2. Inspect `HasAccessRule`, `AddExplicitReadRule`, and production hardening implementation.
3. Determine whether product code removes explicit rule and whether inherited rule comes from parent temp/workspace ACL.
4. Choose smallest safe fix:
   - update test helper/assertion to verify explicit rule removal only, if product hardening contract is explicit-rule removal;
   - or create isolated private-key temp directory with inheritance disabled, if product contract requires no inherited access;
   - or fix production hardening if explicit rule actually remains.
5. Re-run targeted ACL test and full suite.

### 6.2 Allowed fix patterns

- Test-only helper that distinguishes explicit and inherited rules.
- Test fixture setup that creates a controlled ACL environment for private key hardening.
- Production fix only if evidence shows `BackupViaGitService` fails to remove explicit permissive access from configured private key.
- Artifact sync only after validation.

Not allowed:
- Skip/disable the security test.
- Loosen the assertion to pass without checking the hardening contract.
- Broadly changing filesystem ACLs outside the test fixture.

## 7. Бизнес-правила / Алгоритмы

- Private SSH key hardening protects backup credentials from unintended local read access.
- Tests must verify security intent deterministically across Windows ACL inheritance differences.
- Full-suite blocker classification must separate product regression from environment/test-fixture assumptions.

## 8. Точки интеграции и триггеры

- `BackupViaGitService.GetCredentials`.
- `BackupViaGitServiceTests.GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows`.
- Windows `FileSecurity`, inherited and explicit `FileSystemAccessRule` behavior.
- TUnit full-suite process lifecycle.

## 9. Изменения модели данных / состояния

- Product data model: Не применимо.
- Test state: possible temp ACL fixture/helper change.
- STORM state: validation risk notes only.

## 10. Миграция / Rollout / Rollback

- Rollout: test/security hardening fix only.
- Rollback: revert changed test/helper/service files and artifact validation notes.
- Runtime migration: Не применимо.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
- Root cause classified as explicit ACL removal issue, inherited ACL environment behavior, or production hardening regression.
- Targeted `BackupViaGitServiceTests.GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows` passes 1/1.
- Targeted `MainControlResetFiltersUiTests.ResetFiltersButton_IsAvailableOnTaskTabs` remains pass or is documented as separate blocker if it fails.
- Full `Unlimotion.Test` passes, or stops on a different exact blocker after ACL issue is removed.
- `storm.json`, `coverage.md` and `ranking.md` reflect final validation status.

Команды проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/BackupViaGitServiceTests/GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/ResetFiltersButton_IsAvailableOnTaskTabs" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-26-storm-stabilize-backup-acl-full-suite.md
```

## 12. Риски и edge cases

- Риск: inherited ACL differs per Windows profile/workspace.
  - Смягчение: assert the actual hardening contract instead of environment-specific parent ACLs.
- Риск: production code silently leaves explicit read permission.
  - Смягчение: inspect ACL before/after and only adjust test if explicit rule is removed.
- Риск: full-suite reveals another blocker after ACL fix.
  - Смягчение: document next blocker and stop unless within approved scope.

## 13. План выполнения

1. Confirm current diff and validation evidence.
2. Inspect ACL helper and production hardening code.
3. Reproduce targeted ACL failure.
4. Apply minimal fix.
5. Re-run targeted ACL and Headless reset tests.
6. Run full `Unlimotion.Test` once.
7. Sync STORM reports and this SPEC Post-EXEC.
8. Commit if requested.

## 14. Открытые вопросы

Блокирующих вопросов нет. EXEC should begin only after approval phrase.

## 15. Соответствие профилю

- Профиль: QUEST delivery-task + STORM validation follow-up.
- Route: `/storm:cover` continuation with test/security validation changes, so QUEST approval required.
- Product artifacts remain Russian.
- No code/test changes before SPEC approval.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/BackupViaGitServiceTests.cs` | possible ACL helper/assertion fixture fix | Restore deterministic Windows security test |
| `src/Unlimotion/Services/BackupViaGitService.cs` | possible narrow production hardening fix only if evidence requires | Preserve private key hardening behavior |
| `docs/product/storm.json` | validation risk sync | Keep STORM trace current |
| `docs/product/reports/coverage.md` | validation evidence sync | Keep `/storm:cover` report current |
| `docs/product/reports/ranking.md` | next-step sync | Keep recommended next step current |
| `specs/2026-06-26-storm-stabilize-backup-acl-full-suite.md` | Post-EXEC evidence | QUEST trace |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Full-suite validation | 563 total, 561 passed, 2 failed; deterministic ACL blocker plus one Headless dispose observation | target: green full-suite or different documented blocker |
| Targeted ACL evidence | fails 1/1 | target: passes 1/1 |
| Product behavior | unchanged | unchanged unless production hardening regression is proven |

## 18. Альтернативы и компромиссы

- Вариант A: accept full-suite red and rely on targeted STORM evidence.
  - Плюсы: no extra change.
  - Минусы: full-suite remains unreliable gate.
- Вариант B: make ACL security test deterministic while preserving hardening intent.
  - Плюсы: restores trust in full-suite and keeps security coverage.
  - Минусы: requires careful Windows ACL reasoning.
- Вариант C: change production hardening to strip inherited access.
  - Плюсы: stronger security posture if required.
  - Минусы: broader behavior change and possible filesystem compatibility risk.
- Выбран Вариант B unless evidence proves production regression.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Goal, AS-IS, problem, goals and non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | Diagnostic route and allowed fix patterns scoped to ACL/security validation. |
| C. Безопасность изменений | 11-13 | PASS | Stop rules protect production backup behavior. |
| D. Проверяемость | 14-16 | PASS | Targeted and full-suite commands listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan and file scope concrete. |
| F. Соответствие профилю | 20 | PASS | QUEST and STORM route respected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | One primary blocker: Windows ACL hardening test. |
| 2. Понимание текущего состояния | 5 | Uses current full-suite and targeted evidence. |
| 3. Конкретность целевого дизайна | 5 | Explicit vs inherited ACL path concrete. |
| 4. Безопасность | 5 | No skip/disable and no broad ACL changes. |
| 5. Тестируемость | 5 | Commands cover targeted and full-suite validation. |
| 6. Готовность к автономной реализации | 5 | No blocking questions. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: current full-suite log evidence, targeted ACL result, targeted Headless reset result, STORM reports and central routing requirements.
- Decision: можно запрашивать подтверждение.
- Stop decision: wait for `Спеку подтверждаю`.
- Residual risks: full-suite may reveal a different blocker after ACL issue is fixed.

## Approval

Ожидается фраза: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего blocker | 0.9 | Нет | Создать SPEC | Нет | Нет | ServiceStack cleanup process crash закрыт; full-suite теперь блокируется Windows ACL hardening test. | `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`, full-suite log |
| SPEC | Подготовка SPEC и review | 0.88 | Нет | Запросить подтверждение пользователя | Да | Нет | Fix may touch test/security validation behavior, so QUEST approval is required. | `specs/2026-06-26-storm-stabilize-backup-acl-full-suite.md` |
