# STORM Coverage Analysis

Сгенерировано: 2026-07-18
Команда: `/storm:cover -> /storm:bdd-sync -> /storm:bdd-lint`

| Метрика | Значение |
| --- | --- |
| Активные stories со сценариями | 16/16 |
| Acceptance criteria со сценариями и тестами | 44/44 |
| Прошедшие сценарии | 45/45 |
| Scenario -> Test links | 45/45 |
| Сценарии, исполняемые через шаги | 45/45 |
| Определения шагов | 178 |
| Полный serial gate | PASS post-rebase: `Unlimotion.Test` 830/830 за 19m35.329s; Headless UI 33/33 за 1m34.053s |
| Тесты дольше 180 секунд | 0; максимум 35.837 секунды |

## Актуализированные сценарии

| Story | Scenario | Роль покрытия | Фактическое evidence |
| --- | --- | --- | --- |
| `ST-0005` | `SC-0005-002` | business rule | Filter reset executable BDD прошёл 1/1; проверяется независимая status collection каждого tab |
| `ST-0011` | `SC-0011-001` | happy path | Auth contract прошёл 1/1, executable BDD прошёл 1/1 |
| `ST-0011` | `SC-0011-002` | happy path | Server live integration прошёл 2/2, executable BDD прошёл 1/1 |
| `ST-0015` | `SC-0015-002` | constraint check | Platform contracts прошли 3/3, executable BDD прошёл 1/1 |

Lifecycle fixture дополнительно защищена четырьмя регрессионными тестами: concurrent drain, агрегация fault, snapshot barrier и идемпотентный async cleanup. Это test-infrastructure evidence, а не новый продуктовый сценарий.

## Итоговый аудит

- Executable BDD gaps отсутствуют: 45/45.
- Полные наборы подтверждены TUnit HTML/console evidence на `origin/main@75efc04`: failed 0, skipped 0; финальный docs-only rebase на `origin/main@ad90260` не изменил `src`/`tests`/`.github` tree.
- Исторические RED и timeout записи сохранены и не подменены текущим PASS.
- Gherkin, acceptance criteria и test annotations не менялись.
