# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-07-14
Команда: `/storm:rank` + `/storm:bdd-implement SC-0008-003`

`ST-0008` полностью step-executable через `TS-0054..TS-0056`. До полного executable BDD coverage остаётся 14 scenarios. Следующий `/storm:cover` candidate должен быть выбран вне ST-0008 из remaining high-value scenarios; required full-suite gate остаётся отдельным environment risk.
