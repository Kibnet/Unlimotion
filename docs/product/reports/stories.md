# STORM Stories

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-implement SC-0010-001`

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0008 | Все 3/3 scenarios остаются step-executable. | `TS-0054..TS-0056` |
| ST-0010 | `AC-0028`: preview/connect Git remote executable через `TS-0060` и `SD-0135..SD-0138`. | BDD 1/1, preview/connect 4/4 |
| ST-0010 | 1/4 scenarios step-executable; SSH/token, conflicts и backup jobs остаются gaps. | `TS-0008/TS-0009` |

Remaining executable BDD gaps: 10 scenarios. Full suite remains unconfirmed; `BackupViaGitServiceTests` имеет независимый Windows ACL SSH-key sandbox blocker (51/52).
