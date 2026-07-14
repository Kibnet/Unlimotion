# STORM BDD Sync

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0006-001`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 23/45 |
| Новые связи | `SC-0006-001 -> TS-0048 -> SD-0087..SD-0090`; existing `TS-0005` и `TS-0013` сохранены |
| ST-0006 | PARTIAL: `SC-0006-001` step-executable; `SC-0006-002`, `SC-0006-003` остаются gaps |
| Existing test annotations changed | no |
| Feature wording changed | no |
| Automation IDs changed | no |
| Production code changed | no |
| Full suite gate | passed 579/579 on controlled escalated retry |

## Decision Sync

BDD links обновлены для `SC-0006-001`: новый `TS-0048` связывает scenario text с existing planning UI behavior через `SD-0087..SD-0090` и Avalonia.Headless UI contract. Acceptance criteria не заменялись на Gherkin; production code, `.feature` wording, automation IDs и existing test annotations не менялись.

## Оставшиеся gaps

Step definitions покрывают 23/45 scenarios. `ST-0006` теперь 1/3 step-executable; следующие candidates внутри story: `SC-0006-002` repeater и `SC-0006-003` wanted/importance.

UI video evidence uses fallback: current Avalonia.Headless/TUnit runner does not emit safe video artifacts, so targeted headless output and full-suite validation are used as next-best evidence. Initial sandbox full-suite run дал 577/579 из-за sandbox ACL inheritance и unrelated Avalonia.Headless teardown NRE; controlled escalated full retry прошёл 579/579.
