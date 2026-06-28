# STORM BDD Sync

Сгенерировано: 2026-06-29
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0001-003`

## Итог

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 13/45 |
| Новые связи | `SC-0001-003 -> TS-0038 -> SD-0047..SD-0050`; existing `TS-0004` сохранен |
| ST-0001 | fully step-executable: `SC-0001-001`, `SC-0001-002`, `SC-0001-003` closed |
| Existing test annotations changed | no |
| Production code changed | no |
| Feature wording changed | no |

## Decision Sync

BDD links обновлены для `SC-0001-003`: новый `TS-0038` связывает scenario text с existing relation editor, ViewModel and Avalonia.Headless tree-command evidence через `SD-0047..SD-0050`. Acceptance criteria не заменялись на Gherkin; production code, feature wording и existing test annotations не менялись.

## Оставшиеся gaps

Step definitions покрывают 13/45 scenarios. `ST-0001` закрыт полностью на step-executable layer; следующий `/storm:cover` candidate нужно выбрать через актуальный `/storm:rank`.

Full-suite validation сейчас blocked by unrelated filter-flyout UI test cleanup/order issue: `MainControlFilterToolbarResponsiveUiTests/FilterFlyout_EmojiFilters_AllItemTogglesEveryEmojiFilter`. Перед расширением `/storm:cover` нужен отдельный QUEST stabilization slice.
