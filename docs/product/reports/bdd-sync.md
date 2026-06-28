# STORM BDD Sync

Сгенерировано: 2026-06-29
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0001-002`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 12/45 |
| Новые связи | `SC-0001-002 -> TS-0037 -> SD-0043..SD-0046`; existing `TS-0001`, `TS-0014` сохранены |
| ST-0001 | partially step-executable: `SC-0001-001` и `SC-0001-002` закрыты, `SC-0001-003` pending |
| Existing test annotations changed | no |
| Production code changed | no |
| Feature wording changed | no |

## Decision Sync

BDD links обновлены для `SC-0001-002`: новый `TS-0037` связывает scenario text с real ViewModel/storage/projection/headless UI relation evidence через `SD-0043..SD-0046`. Acceptance criteria не заменялись на Gherkin; production code, feature wording и existing test annotations не менялись.

## Оставшиеся gaps

Step definitions покрывают 12/45 scenarios. Следующий highest-ranked candidate по /storm:cover — продолжение `ST-0001`, прежде всего `SC-0001-003`, чтобы закрыть последний task-graph scenario.

Full-suite validation сейчас blocked by unrelated flaky/order-sensitive tests: `FilterFlyout_EmojiFilters_SummaryShowsSelectedEmojiAndOverflowInListOrder` and `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`. Перед расширением `/storm:cover` стоит выполнить отдельный QUEST stabilization slice.
