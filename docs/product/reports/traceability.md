# STORM Traceability

Сгенерировано: 2026-07-18
Команда: `/storm:trace` sync после утверждённой SPEC восстановления полного набора тестов

| Story | AC | Scenario | Test / Step Definition | Status |
| --- | --- | --- | --- | --- |
| ST-0005 | AC-0014 | SC-0005-002 | TS-0006 + TS-0013 + TS-0033; SD-0027..SD-0030 | PASS: filter reset BDD 1/1 |
| ST-0011 | AC-0032 | SC-0011-001 | TS-0017 + TS-0031; SD-0022..SD-0025 | PASS: auth contract 1/1, BDD 1/1 |
| ST-0011 | AC-0033 | SC-0011-002 | TS-0017..TS-0020 + TS-0032; SD-0022..SD-0024 + SD-0026 | PASS: live integration 2/2, BDD 1/1 |
| ST-0015 | AC-0042 | SC-0015-002 | TS-0015 + TS-0024 + TS-0026; SD-0001..SD-0004 | PASS: platform contracts 3/3, BDD 1/1 |
| ST-0008 | AC-0022..AC-0024 | SC-0008-001..003 | TS-0054..TS-0056 | 3/3 прошедших исполняемых BDD |
| ST-0009 | AC-0025 | SC-0009-001 | TS-0014 + TS-0057 + SD-0123..SD-0126 | прошедший исполняемый BDD; прямое сохранение/загрузка JSON |
| ST-0009 | AC-0026 | SC-0009-002 | TS-0003 + TS-0014 + TS-0058 + SD-0127..SD-0130 | прошедший исполняемый BDD; миграция обратных связей, статуса и доступности |
| ST-0009 | AC-0027 | SC-0009-003 | TS-0014 + TS-0059 + SD-0131..SD-0134 | прошедший исполняемый BDD; восстановление JSON и исключение migration reports |
| ST-0010 | AC-0028 | SC-0010-001 | TS-0008 + TS-0009 + TS-0060 + SD-0135..SD-0138 | прошедший исполняемый BDD; preview/connect удалённого Git repository |
| ST-0010 | AC-0029 | SC-0010-002 | TS-0008 + TS-0009 + TS-0061 + SD-0139..SD-0142 | прошедший исполняемый BDD; SSH/token-аутентификация и хранение ключа |
| ST-0010 | AC-0030 | SC-0010-003 | TS-0008 + TS-0009 + TS-0062 + SD-0143..SD-0146 | прошедший исполняемый BDD; разрешение конфликтов файла/полей до commit/push |
| ST-0010 | AC-0031 | SC-0010-004 | TS-0009 + TS-0063 + SD-0147..SD-0150 | прошедший исполняемый BDD; Git jobs, remote pull и сохранение задач |
| ST-0012 | AC-0034 | SC-0012-001 | TS-0008 + TS-0012 + TS-0064 + SD-0151..SD-0154 | прошедший исполняемый BDD; настройка внешнего вида и её применение |
| ST-0012 | AC-0035 | SC-0012-002 | TS-0008 + TS-0009 + TS-0065 + SD-0155..SD-0158 | прошедший исполняемый BDD; готовность storage/Git и действия разрешения конфликтов |
| ST-0012 | AC-0036 | SC-0012-003 | TS-0008 + TS-0015 + TS-0066 + SD-0159..SD-0162 | прошедший исполняемый BDD; состояния обновления, Settings controls и совместимость пакета |
| ST-0013 | AC-0037 | SC-0013-001 | TS-0001 + TS-0004 + TS-0010 + TS-0067 + SD-0163..SD-0166 | прошедший исполняемый BDD; Markdown descriptions, settings и копирование tree command |
| ST-0013 | AC-0038 | SC-0013-002 | TS-0001 + TS-0004 + TS-0010 + TS-0068 + SD-0167..SD-0170 | прошедший исполняемый BDD; parser, подтверждение preview и вставка tree command |
| ST-0015 | AC-0041 | SC-0015-001 | TS-0011 + TS-0015 + TS-0069 + SD-0171..SD-0174 | прошедший исполняемый BDD; WinExe/Velopack workflow contract и startup/update/package UI evidence |
| ST-0015 | AC-0043 | SC-0015-003 | TS-0011 + TS-0015 + TS-0070 + SD-0175..SD-0178 | прошедший исполняемый BDD; CI/media source contract, ReadmeDemo headless smoke и evidence отзывчивости загрузки |

Все ранее существовавшие Story -> AC -> Scenario -> Test -> Step Definition связи сохранены. Общий executable ratio: 45/45; post-rebase serial gates: `Unlimotion.Test` 830/830 и Headless UI 33/33 на `origin/main@75efc04`. Финальный docs-only rebase на `origin/main@ad90260` не изменил `src`/`tests`/`.github` tree.
