# STORM BDD Sync

Сгенерировано: 2026-06-28
Команда: `/storm:bdd-sync` after `/storm:cover Android workload install build smoke`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 7/45 |
| Новые связи | Нет в этой environment/setup итерации; существующие `SC-0011-002 -> TS-0032` и `SC-0011-002 -> SD-0022..SD-0024 + SD-0026` сохранены |
| Draft scenarios | нет |
| Existing test annotations changed | no |
| Tests changed | no |
| Production code changed | no |
| CV-0007 | без изменений: нет active scenario/test links после Варианта B |

## Синхронизировано

| Scenario | Status | Tests | Evidence |
| --- | --- | --- | --- |
| SC-0011-001 | passing | TS-0017, TS-0031 | Auth login/register/refresh-token contract покрыт `TS-0017`; scenario исполняется из `.feature` текста через `SD-0022..SD-0025`. |
| SC-0011-002 | passing | TS-0017, TS-0018, TS-0019, TS-0020, TS-0032 | CRUD/SignalR contract, security regression, live SignalR и live ServiceStack API evidence сохранены; scenario исполняется из `.feature` текста через `SD-0022..SD-0024` и `SD-0026`. |
| SC-0014-001 | passing | TS-0022, TS-0028 | Telegram command/auth coverage остается зеленым; scenario исполняется из `.feature` текста через `SD-0009..SD-0012`. |
| SC-0014-002 | passing | TS-0025, TS-0027 | Telegram Git timers пропускают pull и commit/push, пока идет разрешение конфликтов; scenario исполняется из `.feature` текста через `SD-0005..SD-0008`. |
| SC-0014-003 | passing | TS-0023, TS-0029 | Callback unauthorized/open/status/delete/create prompt/relation behavior покрыт; scenario исполняется из `.feature` текста через `SD-0013..SD-0016`. |
| SC-0015-002 | passing | TS-0015, TS-0024, TS-0026 | Android/browser/iOS project contracts покрыты; scenario исполняется из `.feature` текста через `SD-0001..SD-0004`; Browser Release build smoke прошёл; iOS Debug build smoke прошёл 2026-06-28; Android Debug build smoke прошёл 2026-06-28; runtime release support не заявляется. |
| SC-0016-001 | passing | TS-0021, TS-0030 | Error toast rendering and close UX покрыты; scenario исполняется из `.feature` текста через `SD-0017..SD-0021`. |

## Несинхронизированные Области

| Область | Причина | Следующее действие |
| --- | --- | --- |
| step_definitions | Step definitions есть для seven selected slices; repo-local runner intentionally covers selected scenarios only. | Расширять по отдельным high-value scenarios, не генерировать placeholders. |
| Android/iOS build smoke | Environment-admin SPEC выполнена 2026-06-28: targeted `dotnet workload install android` завершился как no-op, затем Android Debug build smoke прошёл; iOS Debug build smoke тоже прошёл. | Build-smoke gap закрыт; runtime/release claims требуют отдельной SPEC. |
| Runtime/release smoke | Browser build smoke не равен runtime launch/release pipeline evidence. | Отдельная platform runtime/release SPEC, если нужны release support claims. |
| Full-suite validation | Full-suite gate восстановлен предыдущей итерацией: Windows ACL hardening blocker закрыт production fix, full `Unlimotion.Test` проходил 563/563 вне sandbox. Эта environment-admin SPEC full-suite не запускала. | Использовать предыдущий green gate как baseline; текущая итерация была environment/build-smoke only. |
| CV-0007 | Вариант B сохраняет attachment code как internal/orphan contract candidate. | Future revisit only after new product decision. |

## Decision Sync

BDD links сохранены: `SC-0011-002`, `SC-0011-001`, `SC-0014-001`, `SC-0014-002`, `SC-0014-003`, `SC-0015-002` и `SC-0016-001` сохраняют executable связи. `SC-0015-002` теперь имеет Browser, iOS и Android build-smoke evidence без code/test/test-annotation изменений. `CV-0007` намеренно остается вне active Gherkin scenario/test links после Варианта B.
