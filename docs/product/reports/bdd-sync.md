# STORM BDD Sync

Сгенерировано: 2026-06-19
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0016-001 executable step definitions`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 5/45 |
| Новые связи | `SC-0016-001 -> TS-0030`; `SC-0016-001 -> SD-0017..SD-0021` |
| Draft scenarios | нет |
| Test annotations changed | no |
| Tests changed | yes, approved SPEC scope |
| Production code changed | no |
| CV-0007 | без изменений: нет active scenario/test links после Варианта B |

## Синхронизировано

| Scenario | Status | Tests | Evidence |
| --- | --- | --- | --- |
| SC-0014-001 | passing | TS-0022, TS-0028 | Telegram command/auth coverage остается зеленым; scenario исполняется из `.feature` текста через `SD-0009..SD-0012`. |
| SC-0014-002 | passing | TS-0025, TS-0027 | Telegram Git timers пропускают pull и commit/push, пока идет разрешение конфликтов; scenario исполняется из `.feature` текста через `SD-0005..SD-0008`. |
| SC-0014-003 | passing | TS-0023, TS-0029 | Callback unauthorized/open/status/delete/create prompt/relation behavior покрыт; scenario исполняется из `.feature` текста через `SD-0013..SD-0016`. |
| SC-0015-002 | passing | TS-0015, TS-0024, TS-0026 | Android/browser/iOS project contracts покрыты; scenario исполняется из `.feature` текста через `SD-0001..SD-0004`; Browser Release build smoke прошел; Android/iOS build smoke blocked by `NETSDK1147`; runtime release support не заявляется. |
| SC-0016-001 | passing | TS-0021, TS-0030 | Error toast rendering and close UX покрыты; scenario исполняется из `.feature` текста через `SD-0017..SD-0021`. |

## Несинхронизированные Области

| Область | Причина | Следующее действие |
| --- | --- | --- |
| step_definitions | Step definitions есть для five selected slices; repo-local runner intentionally covers selected scenarios only. | Расширять по отдельным high-value scenarios, не генерировать placeholders. |
| SC-0011 passing scenarios | `SC-0011-001` и `SC-0011-002` имеют passing tests, но пока без step definitions. | Отдельная SPEC для server-storage executable slice. |
| Android/iOS build smoke | Локальная среда останавливает Debug build на `NETSDK1147` и предлагает `dotnet workload restore` для `wasm-tools`. | Отдельная environment/setup task; не менять tests/code в текущем BDD slice. |
| Runtime/release smoke | Browser build smoke не равен runtime launch/release pipeline evidence. | Отдельная platform runtime/release SPEC, если нужны release support claims. |
| CV-0007 | Вариант B сохраняет attachment code как internal/orphan contract candidate. | Future revisit only after new product decision. |

## Decision Sync

`SC-0016-001` получил repo-local executable step-definition slice, а `SC-0014-001`, `SC-0014-002`, `SC-0014-003` и `SC-0015-002` сохранили уже реализованные executable связи. Android/iOS build smoke остаются environment blocker, а не product failure. `CV-0007` намеренно остается вне active Gherkin scenario/test links после Варианта B.
