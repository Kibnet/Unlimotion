# STORM Traceability

Сгенерировано: 2026-07-14
Команда: `/storm:trace` sync after `/storm:bdd-implement SC-0010-001`

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0008 | AC-0022..AC-0024 | SC-0008-001..003 | TS-0054..TS-0056 | 3/3 passing executable BDD |
| ST-0009 | AC-0025 | SC-0009-001 | TS-0014 + TS-0057 + SD-0123..SD-0126 | passing executable BDD; direct JSON Save/Load |
| ST-0009 | AC-0026 | SC-0009-002 | TS-0003 + TS-0014 + TS-0058 + SD-0127..SD-0130 | passing executable BDD; migration reverse links/status/availability |
| ST-0009 | AC-0027 | SC-0009-003 | TS-0014 + TS-0059 + SD-0131..SD-0134 | passing executable BDD; JSON recovery and migration reports exclusion |
| ST-0010 | AC-0028 | SC-0010-001 | TS-0008 + TS-0009 + TS-0060 + SD-0135..SD-0138 | passing executable BDD; Git remote preview/connect |

`ST-0009` is 3/3; `ST-0010` is now 1/4 step-executable. Общий executable ratio: 35/45.
