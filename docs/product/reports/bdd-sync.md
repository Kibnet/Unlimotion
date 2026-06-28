# STORM BDD Sync

Сгенерировано: 2026-06-28
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0005-003`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 9/45 |
| Новые связи | `SC-0005-003 -> TS-0034 -> SD-0031..SD-0034`; existing `TS-0006` сохранён |
| Draft scenarios | нет |
| Existing test annotations changed | no |
| Tests changed | да: new executable BDD test/contract/step definitions |
| Production code changed | no |
| Feature wording changed | no |
| CV-0007 | без изменений: нет active scenario/test links после Варианта B |

## Синхронизировано

| Scenario | Status | Tests | Evidence |
| --- | --- | --- | --- |
| SC-0005-002 | passing | TS-0006, TS-0013, TS-0033 | Reset filters behavior step-executable через `SD-0027..SD-0030`. |
| SC-0005-003 | passing | TS-0006, TS-0034 | Emoji include/exclude filter behavior покрыт existing UI suite; scenario теперь исполняется через `SD-0031..SD-0034` и `StormEmojiFilterExecutableSpecTests` прошёл 1/1. |
| SC-0011-001 | passing | TS-0017, TS-0031 | Auth login/register/refresh-token contract покрыт; scenario исполняется через `SD-0022..SD-0025`. |
| SC-0011-002 | passing | TS-0017, TS-0018, TS-0019, TS-0020, TS-0032 | CRUD/SignalR evidence сохранён; scenario исполняется через `SD-0022..SD-0024` и `SD-0026`. |
| SC-0014-001 | passing | TS-0022, TS-0028 | Telegram command/auth coverage; scenario исполняется через `SD-0009..SD-0012`. |
| SC-0014-002 | passing | TS-0025, TS-0027 | Telegram Git timer conflict-safety; scenario исполняется через `SD-0005..SD-0008`. |
| SC-0014-003 | passing | TS-0023, TS-0029 | Callback behavior; scenario исполняется через `SD-0013..SD-0016`. |
| SC-0015-002 | passing | TS-0015, TS-0024, TS-0026 | Android/browser/iOS project contracts and build-smoke evidence; runtime release support не заявляется. |
| SC-0016-001 | passing | TS-0021, TS-0030 | Error toast rendering and close UX; scenario исполняется через `SD-0017..SD-0021`. |

## Несинхронизированные Области

| Область | Причина | Следующее действие |
| --- | --- | --- |
| step_definitions | Step definitions есть для 9 selected slices; repo-local runner intentionally covers selected scenarios only. | Расширять по отдельным high-value scenarios, не генерировать placeholders. |
| ST-0005 remaining scenario | `SC-0005-001` имеет linked tests, но пока без executable step definitions. | Следующая SPEC может взять search/fuzzy scenario. |
| Runtime/release smoke | Build smoke не равен runtime launch/release pipeline evidence. | Отдельная platform runtime/release SPEC, если нужны release support claims. |
| Full-suite validation | Targeted BDD/emoji suites прошли; final full `Unlimotion.Test` вне sandbox прошёл 565/565. | Использовать full-suite evidence вне sandbox как текущий gate. |
| CV-0007 | Вариант B сохраняет attachment code как internal/orphan contract candidate. | Future revisit only after new product decision. |

## Decision Sync

BDD links обновлены для `SC-0005-003`: новый `TS-0034` связывает scenario text с real Avalonia.Headless emoji filter behavior через `SD-0031..SD-0034`. Acceptance criteria не заменялись на Gherkin; production code, feature wording и existing test annotations не менялись.
