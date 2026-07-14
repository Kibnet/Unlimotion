# STORM Traceability

Сгенерировано: 2026-07-14
Команда: `/storm:trace` sync after `/storm:bdd-implement SC-0015-001`

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0008 | AC-0022..AC-0024 | SC-0008-001..003 | TS-0054..TS-0056 | 3/3 passing executable BDD |
| ST-0009 | AC-0025 | SC-0009-001 | TS-0014 + TS-0057 + SD-0123..SD-0126 | passing executable BDD; direct JSON Save/Load |
| ST-0009 | AC-0026 | SC-0009-002 | TS-0003 + TS-0014 + TS-0058 + SD-0127..SD-0130 | passing executable BDD; migration reverse links/status/availability |
| ST-0009 | AC-0027 | SC-0009-003 | TS-0014 + TS-0059 + SD-0131..SD-0134 | passing executable BDD; JSON recovery and migration reports exclusion |
| ST-0010 | AC-0028 | SC-0010-001 | TS-0008 + TS-0009 + TS-0060 + SD-0135..SD-0138 | passing executable BDD; Git remote preview/connect |
| ST-0010 | AC-0029 | SC-0010-002 | TS-0008 + TS-0009 + TS-0061 + SD-0139..SD-0142 | passing executable BDD; SSH/token remote authentication and key storage |
| ST-0010 | AC-0030 | SC-0010-003 | TS-0008 + TS-0009 + TS-0062 + SD-0143..SD-0146 | passing executable BDD; file/field conflict resolution before commit/push |
| ST-0010 | AC-0031 | SC-0010-004 | TS-0009 + TS-0063 + SD-0147..SD-0150 | passing executable BDD; Git jobs, remote pull and task preservation |
| ST-0012 | AC-0034 | SC-0012-001 | TS-0008 + TS-0012 + TS-0064 + SD-0151..SD-0154 | passing executable BDD; appearance setting and effect |
| ST-0012 | AC-0035 | SC-0012-002 | TS-0008 + TS-0009 + TS-0065 + SD-0155..SD-0158 | passing executable BDD; storage/Git readiness and conflict actions |
| ST-0012 | AC-0036 | SC-0012-003 | TS-0008 + TS-0015 + TS-0066 + SD-0159..SD-0162 | passing executable BDD; update states, Settings controls and package compatibility |
| ST-0013 | AC-0037 | SC-0013-001 | TS-0001 + TS-0004 + TS-0010 + TS-0067 + SD-0163..SD-0166 | passing executable BDD; Markdown descriptions, settings and tree-command copy |
| ST-0013 | AC-0038 | SC-0013-002 | TS-0001 + TS-0004 + TS-0010 + TS-0068 + SD-0167..SD-0170 | passing executable BDD; parser, preview confirmation and tree-command paste |
| ST-0015 | AC-0041 | SC-0015-001 | TS-0011 + TS-0015 + TS-0069 + SD-0171..SD-0174 | прошедший исполняемый BDD; WinExe/Velopack workflow contract и startup/update/package UI evidence |

`ST-0009` покрыта 3/3; `ST-0010` покрыта 4/4; `ST-0012` покрыта 3/3; `ST-0013` покрыта 2/2; `ST-0015` покрыта 2/3 исполняемых сценариев. Общий executable ratio: 44/45.
