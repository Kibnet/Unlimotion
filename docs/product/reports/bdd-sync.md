# STORM BDD Sync

Сгенерировано: 2026-07-10
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0004-003`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 22/45 |
| Новые связи | `SC-0004-003 -> TS-0047 -> SD-0083..SD-0086`; existing `TS-0004` и `TS-0011` сохранены |
| ST-0004 | PASS: `SC-0004-001`, `SC-0004-002`, `SC-0004-003` step-executable |
| Existing test annotations changed | no |
| Feature wording changed | no |
| Automation IDs changed | no |
| Production code changed | no |
| Full suite gate | passed 578/578 on controlled retry |

## Decision Sync

BDD links обновлены для `SC-0004-003`: новый `TS-0047` связывает scenario text с existing tree command UI behavior через `SD-0083..SD-0086` и Avalonia.Headless UI contract. Acceptance criteria не заменялись на Gherkin; production code, `.feature` wording, automation IDs и existing test annotations не менялись.

## Оставшиеся gaps

Step definitions покрывают 22/45 scenarios. `ST-0004` теперь 3/3 step-executable; следующий `/storm:cover` candidate нужно выбирать вне `ST-0004` по backlog/rank.

UI video evidence uses fallback: current Avalonia.Headless/TUnit runner does not emit safe video artifacts, so targeted headless output and full-suite validation are used as next-best evidence. Initial full-suite run дал 577/578 из-за unrelated Avalonia.Headless teardown NRE; isolated proof прошёл 1/1, controlled retry прошёл 578/578 with `C:\tmp\unlimotion-full-suite-sc0004-tree-commands-bdd-retry.log`.
