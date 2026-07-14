# STORM Stories

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-implement SC-0012-003`

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0008 | Все 3/3 scenarios остаются step-executable. | `TS-0054..TS-0056` |
| ST-0012 | `AC-0034`: appearance executable через `TS-0064` и `SD-0151..SD-0154`. | BDD 1/1, persistence 4/4, fuzzy UI 1/1 |
| ST-0012 | `AC-0035`: storage, Git backup и conflict actions executable через `TS-0065` и `SD-0155..SD-0158`. | BDD 1/1, VM 3/3, Settings UI 1/1 |
| ST-0012 | `AC-0036`: update controls и compatibility executable через `TS-0066` и `SD-0159..SD-0162`. | BDD 1/1, VM 3/3, Settings/package UI 2/2 |
| ST-0012 | 3/3 scenarios step-executable. | `TS-0064..TS-0066` |

Remaining executable BDD gaps: 4 scenarios. Full suite remains unconfirmed; Windows ACL SSH-key hardening остаётся отдельным sandbox-sensitive evidence.
