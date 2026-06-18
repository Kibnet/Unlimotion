# STORM BDD Sync

Сгенерировано: 2026-06-18
Команда: `/storm:bdd-sync` after `/storm:platform-runtime-validation ST-0015`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Новые связи | нет; обновлено evidence для `SC-0015-002` |
| Draft scenarios | нет |
| Test annotations changed | no |
| Tests changed | no |
| Production code changed | no |
| CV-0007 | без изменений: нет active scenario/test links после Варианта B |

## Синхронизировано

| Scenario | Status | Tests | Evidence |
| --- | --- | --- | --- |
| SC-0014-001 | passing | TS-0022 | Telegram command/auth coverage остается зеленым. |
| SC-0014-002 | passing | TS-0025 | Telegram Git timers пропускают pull и commit/push, пока идет разрешение конфликтов. |
| SC-0014-003 | passing | TS-0023 | Callback unauthorized/open/status/delete/create prompt/relation behavior покрыт. |
| SC-0015-002 | passing | TS-0015, TS-0024 | Android/browser/iOS project contracts покрыты; Browser Release build smoke прошел; Android/iOS build smoke blocked by `NETSDK1147`; runtime release support не заявляется. |

## Несинхронизированные Области

| Область | Причина | Следующее действие |
| --- | --- | --- |
| step_definitions | Step definitions намеренно остаются пустыми; текущий repo использует TUnit handler tests вместо executable Gherkin runner. | Добавлять только если executable Gherkin runner станет частью workflow. |
| Android/iOS build smoke | Локальная среда останавливает Debug build на `NETSDK1147` и предлагает `dotnet workload restore` для `wasm-tools`. | Отдельная environment/setup task; не менять tests/code в artifact-only sync. |
| Runtime/release smoke | Browser build smoke не равен runtime launch/release pipeline evidence. | Отдельная platform runtime/release SPEC, если нужны release support claims. |
| CV-0007 | Вариант B сохраняет attachment code как internal/orphan contract candidate. | Future revisit only after new product decision. |

## Decision Sync

`ST-0015 / SC-0015-002` получил Browser Release build smoke evidence. Android/iOS build smoke зафиксированы как environment blocker, а не как product failure. `CV-0007` намеренно остается вне active Gherkin scenario/test links после Варианта B.
