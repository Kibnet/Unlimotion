# STORM BDD Sync

Сгенерировано: 2026-06-18
Команда: `/storm:bdd-sync` after `/storm:cover CV-0007 attachment workflow confirmation`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 44/45 |
| New links | none |
| Draft scenarios | SC-0014-002 |
| Test annotations changed | no |
| Tests changed | no |
| Production behavior changed | no |
| CV-0007 | no active scenario/test links; blocked pending product decision |

## Синхронизировано

| Scenario | Status | Tests | Evidence |
| --- | --- | --- | --- |
| SC-0014-001 | passing | TS-0022 | Telegram command/auth coverage remains green. |
| SC-0014-003 | passing | TS-0023 | Callback unauthorized/open/status/delete/create prompt/relation behavior covered 7/7. |
| SC-0015-002 | passing | TS-0015, TS-0024 | Android/browser/iOS project contracts covered 3/3; runtime release support is not claimed. |
| SC-0014-002 | draft | нет | Git timer/conflict-safety part remains split gap; no test/code implementation added. |

## Несинхронизированные Области

| Область | Причина | Следующее действие |
| --- | --- | --- |
| CV-0007 attachment workflow | Attachment backend/API code exists, but no confirmed story/AC/UI/docs evidence establishes user-facing workflow. | Product decision first; `/storm:expand` or `/storm:bdd-implement` only after confirmation. |
| SC-0014-002 Git timers | Current TelegramBot StartTimers directly invokes GitService pull/push timers and has no conflict-resolution guard to cover as existing behavior. | Separate `/storm:bdd-implement` if product-supported. |
| step_definitions | Step definitions intentionally remain empty; current repo uses TUnit handler tests rather than executable Gherkin runner. | Add only if executable Gherkin runner becomes part of workflow. |
| Android/browser/iOS runtime smoke | `TS-0024` covers project contracts only; optional local builds were blocked by restore/workload state. | Separate platform validation task if release/runtime evidence is needed. |
