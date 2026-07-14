# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-07-14
Команда: `/storm:rank` + `/storm:bdd-implement SC-0008-001`

`SC-0008-001` закрыт через `TS-0054`; до полного executable BDD coverage остаётся 16 scenarios. Следующий `/storm:cover` candidate: `SC-0008-002` (`AC-0023 / GR-023`) — viewport и overlay states имеют существующую UI suite `TS-0007`, остаются в той же story и не требуют product/config changes. Required full-suite gate остаётся отдельным environment risk.
