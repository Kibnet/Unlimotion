# STORM Stories

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-implement SC-0015-001`

| Story | Изменение | Доказательства |
| --- | --- | --- |
| ST-0008 | Все 3/3 scenarios остаются step-executable. | `TS-0054..TS-0056` |
| ST-0012 | `AC-0034`: appearance executable через `TS-0064` и `SD-0151..SD-0154`. | BDD 1/1, persistence 4/4, fuzzy UI 1/1 |
| ST-0012 | `AC-0035`: storage, Git backup и conflict actions executable через `TS-0065` и `SD-0155..SD-0158`. | BDD 1/1, VM 3/3, Settings UI 1/1 |
| ST-0012 | `AC-0036`: update controls и compatibility executable через `TS-0066` и `SD-0159..SD-0162`. | BDD 1/1, VM 3/3, Settings/package UI 2/2 |
| ST-0012 | 3/3 scenarios step-executable. | `TS-0064..TS-0066` |
| ST-0013 | `AC-0037`: Markdown outline с descriptions и выбранное поддерево executable через `TS-0067` и `SD-0163..SD-0166`. | BDD 1/1, service/VM/UI 3/3 |
| ST-0013 | `AC-0038`: preview/import и дерево после подтверждения executable через `TS-0068` и `SD-0167..SD-0170`. | BDD 1/1, parser/VM/UI 3/3 |
| ST-0013 | 2/2 scenarios step-executable. | `TS-0067..TS-0068` |
| ST-0015 | `AC-0041`: desktop WinExe/package contract исполняем через `TS-0069` и `SD-0171..SD-0174`. | BDD 1/1, Desktop Release build, startup/update/package UI 3/3 |
| ST-0015 | 2/3 сценария исполняемы через шаги; `SC-0015-003` остаётся разрывом. | `TS-0011`, `TS-0015`, `TS-0024`, `TS-0069` |

Оставшиеся исполняемые BDD-разрывы: 1 сценарий. Full suite остаётся неподтверждённым; Windows ACL SSH-key hardening остаётся отдельным sandbox-sensitive evidence.
