# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-07-14
Команда: `/storm:rank` + `/storm:bdd-implement SC-0008-002`

`SC-0008-001/002` закрыты через `TS-0054/TS-0055`; до полного executable BDD coverage остаётся 15 scenarios. Следующий `/storm:cover` candidate: `SC-0008-003` (`AC-0024 / GR-024`) — filters, inline rename, multi-selection и remaining overlay/minimap controls имеют существующие tests `TS-0006/TS-0007`, завершают story-level coverage и не требуют product/config changes. Required full-suite gate остаётся отдельным environment risk.
