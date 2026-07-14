# STORM Stories

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-implement SC-0010-002`

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0008 | Все 3/3 scenarios остаются step-executable. | `TS-0054..TS-0056` |
| ST-0010 | `AC-0029`: SSH/token remote-аутентификация и key storage executable через `TS-0061` и `SD-0139..SD-0142`. | BDD 1/1, SSH/token/key-storage 4/4 |
| ST-0010 | 2/4 scenarios step-executable; conflicts и backup jobs остаются gaps. | `TS-0008/TS-0009` |

Remaining executable BDD gaps: 9 scenarios. Full suite remains unconfirmed; Windows ACL SSH-key hardening остаётся отдельным sandbox-sensitive evidence.
