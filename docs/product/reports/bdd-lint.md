# STORM BDD Lint

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0010-001`

passed_with_warnings

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | PASS: 45/45; `SC-0010-001` has `TS-0060` and preserves `TS-0008/TS-0009` |
| Scenario -> Step Definition links | WARNING: 35/45 step-executable |
| ST-0010 | PARTIAL: `SC-0010-001` executable; `SC-0010-002/003/004` remain gaps |
| Feature/production/annotations | PASS: no changes |
| Targeted gate | PASS: BDD 1/1, preview/connect methods 4/4 |
| Full suite | NOT RUN: previous timeout has no summary |

Полный `BackupViaGitServiceTests` дал 51/52: ACL-проверка SSH key не прошла в текущем Windows sandbox. Она не относится к `SC-0010-001`; все четыре связанные preview/connect проверки прошли.
