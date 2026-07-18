# STORM BDD Lint

Сгенерировано: 2026-07-18
Команда: `/storm:bdd-lint` после post-rebase валидации утверждённой SPEC

Статус: `passed_with_warnings`

| Проверка | Результат |
| --- | --- |
| Scenario -> Test | PASS: 45/45 |
| Scenario -> Step Definition | PASS: 45/45 |
| Активные сценарии без links | PASS: 0 |
| Формулировки `.feature` и acceptance criteria | PASS: не менялись |
| Test annotations | PASS: не менялись |
| Targeted gate | PASS: lifecycle 4/4; Telegram status 11/11; callback 7/7 + BDD 1/1; tree-search 7/7; filter BDD 1/1; wanted 2/2; relation add и delete 1/1 |
| Полный serial gate | PASS: `Unlimotion.Test` 830/830; Headless UI 33/33; failed 0, skipped 0 |
| Ограничение 180 секунд | PASS: превышений 0; максимум 35.837 секунды |
| Центральный validator | PASS: 0 errors, 18 известных warnings; executable specification 45/45, step reuse 181/181 |

Остаются 18 известных неблокирующих предупреждений о намеренном повторном использовании общих Given/When шагов. Новых lint gaps не обнаружено.
