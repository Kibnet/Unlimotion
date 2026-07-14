# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-07-14
Команда: `/storm:rank` + `/storm:bdd-implement SC-0007-003`

`ST-0007` полностью step-executable через `TS-0051..TS-0053`. До полного executable BDD coverage остаётся 17 scenarios. Следующий `/storm:cover` candidate должен быть выбран вне ST-0007 из remaining high-value scenarios; required full-suite gate остаётся отдельным environment risk.
