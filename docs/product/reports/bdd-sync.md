# STORM BDD Sync

Сгенерировано: 2026-06-28
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0005-002`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 8/45 |
| Новые связи | `SC-0005-002 -> TS-0033 -> SD-0027..SD-0030`; existing `TS-0006` и `TS-0013` сохранены |
| Draft scenarios | нет |
| Existing test annotations changed | no |
| Tests changed | да: new executable BDD test/contract/step definitions |
| Production code changed | no |
| Feature wording changed | no |
| CV-0007 | без изменений: нет active scenario/test links после Варианта B |

## Синхронизировано

| Scenario | Status | Tests | Evidence |
| --- | --- | --- | --- |
| SC-0005-002 | passing | TS-0006, TS-0013, TS-0033 | Reset filters behavior покрыт existing UI suites; scenario теперь исполняется из `.feature` текста через `SD-0027..SD-0030` и `StormFilterResetExecutableSpecTests` прошёл 1/1. |
| SC-0011-001 | passing | TS-0017, TS-0031 | Auth login/register/refresh-token contract покрыт `TS-0017`; scenario исполняется через `SD-0022..SD-0025`. |
| SC-0011-002 | passing | TS-0017, TS-0018, TS-0019, TS-0020, TS-0032 | CRUD/SignalR contract, security regression, live SignalR и live ServiceStack API evidence сохранены; scenario исполняется через `SD-0022..SD-0024` и `SD-0026`. |
| SC-0014-001 | passing | TS-0022, TS-0028 | Telegram command/auth coverage остается зеленым; scenario исполняется через `SD-0009..SD-0012`. |
| SC-0014-002 | passing | TS-0025, TS-0027 | Telegram Git timers skip conflict-sensitive operations; scenario исполняется через `SD-0005..SD-0008`. |
| SC-0014-003 | passing | TS-0023, TS-0029 | Callback behavior покрыт; scenario исполняется через `SD-0013..SD-0016`. |
| SC-0015-002 | passing | TS-0015, TS-0024, TS-0026 | Android/browser/iOS project contracts покрыты; scenario исполняется через `SD-0001..SD-0004`; Browser/iOS/Android build smoke прошёл; runtime release support не заявляется. |
| SC-0016-001 | passing | TS-0021, TS-0030 | Error toast rendering and close UX покрыты; scenario исполняется через `SD-0017..SD-0021`. |

## Несинхронизированные Области

| Область | Причина | Следующее действие |
| --- | --- | --- |
| step_definitions | Step definitions есть для 8 selected slices; repo-local runner intentionally covers selected scenarios only. | Расширять по отдельным high-value scenarios, не генерировать placeholders. |
| ST-0005 remaining scenarios | `SC-0005-001` и `SC-0005-003` имеют linked tests, но пока без executable step definitions. | Следующая SPEC может взять один из этих scenarios. |
| Runtime/release smoke | Build smoke не равен runtime launch/release pipeline evidence. | Отдельная platform runtime/release SPEC, если нужны release support claims. |
| Full-suite validation | Sandbox full run упал на Windows ACL inheritance; targeted ACL rerun вне sandbox прошёл; final full `Unlimotion.Test` вне sandbox прошёл 564/564. | Использовать full-suite evidence вне sandbox как текущий gate. |
| CV-0007 | Вариант B сохраняет attachment code как internal/orphan contract candidate. | Future revisit only after new product decision. |

## Decision Sync

BDD links обновлены для `SC-0005-002`: новый `TS-0033` связывает scenario text с real Avalonia.Headless reset-filter behavior через `SD-0027..SD-0030`. Acceptance criteria не заменялись на Gherkin; production code, feature wording и existing test annotations не менялись.
