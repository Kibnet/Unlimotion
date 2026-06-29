# STORM BDD Sync

Сгенерировано: 2026-06-29
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0002-001 + stability gate`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 14/45 |
| Новые связи | `SC-0002-001 -> TS-0039 -> SD-0051..SD-0054`; existing `TS-0003` и `TS-0005` сохранены |
| ST-0002 | partial: `SC-0002-001` step-executable; `SC-0002-002`, `SC-0002-003` остаются linked automated tests без step definitions |
| Existing test annotations changed | no |
| Feature wording changed | no |
| Production behavior changed | no |
| Full suite gate | passed 570/570 |

## Decision Sync

BDD links обновлены для `SC-0002-001`: новый `TS-0039` связывает scenario text с existing TaskStatus domain/ViewModel/filter evidence через `SD-0051..SD-0054`. Acceptance criteria не заменялись на Gherkin; `.feature` wording и existing test annotations не менялись.

Для закрытия обязательного full-suite gate выполнен отдельный scoped stability fix: `TaskItemViewModel.Update(TaskItem)` больше не запускает autosave во время model-sync, UI outline setup не создаёт параллельный autosave, а package compatibility smoke читает актуальные relation VM из repository.

## Оставшиеся gaps

Step definitions покрывают 14/45 scenarios. `ST-0002` продолжает `/storm:cover` с двумя кандидатами без step definitions: `SC-0002-002` и `SC-0002-003`.

Full-suite validation восстановлен: `Unlimotion.Test` проходит 570/570 вне managed sandbox, лог `C:\tmp\unlimotion-full-suite-sc0002-status-support-bdd-final2.log`.
