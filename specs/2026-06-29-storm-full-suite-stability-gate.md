# STORM: full-suite stability gate перед продолжением /storm:cover

## 1. Метаданные

- Статус: Draft for review -> auto-approved by active goal.
- Тип: QUEST `delivery-task` / STORM validation stabilization.
- Дата: 2026-06-29.
- Автор: Codex.
- Связанные артефакты: `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/bdd-lint.md`.
- Связанные тесты: `MainControlFilterToolbarResponsiveUiTests/FilterFlyout_EmojiFilters_SummaryShowsSelectedEmojiAndOverflowInListOrder`, `MainWindowViewModelTests/PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`, full `Unlimotion.Test`.

## 2. Контекст и проблема

После delivery slice `SC-0001-002` targeted checks прошли, но full-suite gate вне managed sandbox не стал надежным:

- Первый full run: `566/568`, failures in unrelated `FilterFlyout_EmojiFilters_SummaryShowsSelectedEmojiAndOverflowInListOrder` and `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`.
- Изолированный retry для filter flyout прошел.
- Изолированный retry для paste outline сначала упал, затем прошел.
- Короткий stress loop после коммита прошел 6/6 по двум тестам.
- Второй full-suite retry timed out after 604 seconds before progress beyond test-run start; leftover runner process was stopped.

Это блокирует следующий широкий `/storm:cover` шаг: нельзя уверенно отличать regression нового scenario coverage от order/flaky full-suite noise.

## 3. Цель

Восстановить надежный validation gate перед продолжением `/storm:cover`:

1. Выполнить controlled full-suite retry вне managed sandbox with captured log and longer timeout.
2. Если full suite проходит, сделать artifact-only sync: снять full-suite blocker из STORM reports and metrics.
3. Если full suite снова падает на тех же unrelated tests, выполнить только минимальную test-only stabilization после локального воспроизведения.
4. Если для фикса нужен product behavior change, остановиться и открыть отдельную SPEC.

## 4. Non-Goals

- Не добавлять новый `/storm:cover` scenario в этой SPEC.
- Не менять product behavior.
- Не менять existing test annotations.
- Не менять `.feature` wording.
- Не скрывать failing tests через skip/ignore.
- Не считать full suite green по targeted checks only.

## 5. AS-IS Evidence

| Evidence | Result |
| --- | --- |
| `StormMultipleParentsRelationExecutableSpecTests` | passed 1/1 |
| Linked `SC-0001-002` targeted tests | passed |
| `validate-artifacts.py docs\product\storm.json` | OK: 0 errors, 4 intentional duplicate-step warnings |
| Full suite first run | failed 566/568 on two unrelated tests |
| Targeted stress loop | passed 6/6 |
| Full suite retry | timed out before progress |

## 6. Target Design

### Path A: full suite passes

- Update `docs/product/storm.json`:
  - `behavior_coverage_metrics.full_suite_result` -> passed full `Unlimotion.Test`.
  - `coverage_analysis.full_suite_result` -> same.
  - validation evidence includes exact command and log path.
- Update reports:
  - `coverage.md`: full suite passed after controlled retry; no blocker.
  - `bdd-sync.md` / `bdd-lint.md`: remove or downgrade full-suite blocker note.
- No test/code changes.

### Path B: failure reproduces

- Re-run failing tests as narrow filters.
- If reproducible and test-only fix is obvious, make the smallest test-only stabilization.
- Run the affected target tests repeatedly.
- Run full suite again.
- Update artifacts with actual result.

### Path C: product/code behavior needed

- Stop before implementation.
- Create a new SPEC for product behavior or shared test infrastructure change.

## 7. Files Allowed To Change

Path A allowed files:

- `docs/product/storm.json`
- `docs/product/reports/coverage.md`
- `docs/product/reports/bdd-sync.md`
- `docs/product/reports/bdd-lint.md`
- this SPEC

Path B additionally allowed only if failure reproduces and fix is test-only:

- `src/Unlimotion.Test/**/*.cs`

Any production project under `src/Unlimotion*` outside tests is out of scope.

## 8. Validation Plan

1. Controlled full-suite retry:
   - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed *> C:\tmp\unlimotion-full-suite-stability-gate.log`
2. If timeout/failure:
   - inspect `C:\tmp\unlimotion-full-suite-stability-gate.log`;
   - verify no leftover `dotnet` runner remains.
3. Targeted repro if needed:
   - `FilterFlyout_EmojiFilters_SummaryShowsSelectedEmojiAndOverflowInListOrder`
   - `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`
4. Artifact gates:
   - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
   - `git diff --check`
   - `rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-29-storm-full-suite-stability-gate.md`

## 9. Risks

- Full suite may hang before producing detailed logs. Mitigation: timeout, process cleanup, report exact state.
- Failures may be order-dependent and not reproducible as narrow filters. Mitigation: do not invent fixes; record evidence and isolate in a separate stabilization plan if needed.
- Test-only stabilization can mask product defects. Mitigation: no skip/ignore; no product behavior changes under this SPEC.

## 10. Acceptance Criteria

1. Full-suite gate result is updated from "blocked" to one of:
   - passed with exact evidence, or
   - still blocked with sharper reproducible blocker evidence.
2. No production code changes are made.
3. STORM reports and `storm.json` agree on the final gate state.
4. Validator and hygiene gates pass.
5. If code/test changes are made, they are limited to test-only stabilization and committed separately.

## 11. SPEC Linter Result

| Блок | Статус | Комментарий |
| --- | --- | --- |
| Полнота | PASS | Цель, AS-IS evidence, target paths and validation plan defined. |
| Scope control | PASS | Product behavior and new `/storm:cover` scenarios excluded. |
| Safety | PASS | Stop rule for production changes and no skip/ignore. |
| Testability | PASS | Exact commands and expected artifact updates listed. |

Итог: ГОТОВО

## 12. SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Ясность цели | 5 | One gate stabilization objective. |
| Понимание AS-IS | 5 | Exact failures and retry evidence listed. |
| Дизайн | 5 | Path A/B/C avoids premature code changes. |
| Безопасность | 5 | No product behavior, no skip/ignore. |
| Проверяемость | 5 | Commands and artifact gates concrete. |
| Автономность | 5 | Active goal auto-approves execution. |

Итоговый балл: 30 / 30

## 13. Post-SPEC Review

- Статус: PASS.
- Scope reviewed: previous full-suite logs, targeted retry evidence, central STORM/QUEST/testing profile, local UI-test override.
- Decision: execute Path A first. Switch to Path B only on reproducible failure.
- Findings requiring spec edits: none.
- Residual risk: full-suite timeout may remain environmental/order-sensitive; report explicitly if it cannot be resolved inside this scope.

## 14. Approval

Получено автоматически из активной цели пользователя: "я автоматически спеку подтверждаю".

## 15. Post-EXEC Review

- Статус: PASS, Path A выполнен.
- Scope reviewed: `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/bdd-lint.md`, full-suite log `C:\tmp\unlimotion-full-suite-stability-gate.log`.
- Реализация: test/code changes не потребовались; выполнен controlled full-suite retry and artifact-only sync.
- Validation:
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed *> C:\tmp\unlimotion-full-suite-stability-gate.log` passed 568/568 in 7m30s outside managed sandbox.
  - No leftover `dotnet` runner processes after the run.
- Decision: full-suite blocker снят; можно возвращаться к `/storm:cover`, next candidate `SC-0001-003`.
- Residual risk: earlier unrelated failures remain classified as transient flaky/order-sensitive signal; if they recur, reopen with a narrower test-stability SPEC.

## 16. Журнал действий агента

| Фаза | Сценарий | Уверенность | Следующее действие | Нужен человек | Объяснение |
| --- | --- | ---: | --- | --- | --- |
| SPEC | Full-suite gate stabilization | 0.82 | Controlled full-suite retry | Нет | Current blocker is validation reliability, not missing scenario coverage. |
| EXEC | Path A artifact-only sync | 0.9 | Validate artifacts and commit | Нет | Controlled retry passed 568/568; no test/code fix required. |
