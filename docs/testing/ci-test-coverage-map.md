# Исполняемые сценарии и устранённые дубли

Рабочая база: `f39b32458aba0f7fe403b3bea26c14f9215d0507`. Связанные feature/scenario/TS IDs сохранены. Снятие отдельного `[Test]` означает перенос владения исполнением в BDD, а не удаление assertions.

| Прежний класс / метод | Единственный entry point пакета | Helper / сохранённые проверки |
| --- | --- | --- |
| `ServerStorageLiveIntegrationTests.ServerStorage_LiveSignalR_SaveTask_DeliversUpdateToSecondClientForSameUser` | `StormServerStorageCrudRealtimeExecutableSpecTests.ServerStorageCrudRealtimeScenario_ExecutesFeatureSteps` | `ServerStorageCrudRealtimeContract.AssertLiveSignalRSaveTaskDeliversUpdateToSecondClientForSameUserAsync`: настоящий Kestrel/RavenDB, два аутентифицированных клиента, доставка и отсутствие sender echo |
| `ServerStorageLiveIntegrationTests.ServerStorage_LiveServiceStackTaskApi_BulkInsertGetAllAndGetTask_RoundTripsAuthenticatedUserTasks` | тот же server BDD | `AssertLiveServiceStackTaskApiRoundTripsAuthenticatedUserTasksAsync`: реальные BulkInsert/GetAll/GetTask, пользовательская область данных |
| `MainControlFilterToolbarResponsiveUiTests.Toolbar_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing` | `StormEmojiFilterExecutableSpecTests.EmojiFilterScenario_ExecutesFeatureSteps` | `MainControlFilterToolbarResponsiveUiTests.EmojiScenarios`: include/exclude, поиск, переключение, сохранение popup |
| `MainControlFilterToolbarResponsiveUiTests.Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter` | тот же emoji BDD | All on/off, выбор строки, Space, popup |
| `MainControlFilterToolbarResponsiveUiTests.Toolbar_EmojiFilters_NoMatchesShowsWarningAndKeepsFullList` | тот же emoji BDD | No matches, предупреждение, полный список |
| `MainControlFilterToolbarResponsiveUiTests.Toolbar_EmojiFilters_KeyboardFlowOpensSearchTogglesAndClosesPopup` | тот же emoji BDD | Реальный keyboard flow |
| `MainControlFilterToolbarResponsiveUiTests.RoadmapToolbar_EmojiFilters_UsesSearchableMultiSelectDropdown` | тот же emoji BDD | Реальный dropdown панели roadmap |
| `MainControlTreeCommandsUiTests.TreeSearch_AllTasksSearchEditor_FiltersVisibleTree` | `StormSearchBehaviorExecutableSpecTests.SearchBehaviorScenario_ExecutesFeatureSteps` | `MainControlTreeCommandsUiTests.SearchScenario`: реальный SearchEditor, filtered tree, очистка/возврат |
| `RoadmapGraphUiTests.RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode` | тот же search BDD; также собственный `StormRoadmapInteractionsExecutableSpecTests` | `RoadmapGraphUiTests.SearchScenario`: exact/fuzzy, очистка, сохранение node identity/selection и отсутствие rebuild |

Второй Roadmap BDD остаётся самостоятельным пользовательским сценарием. Общий helper выполняется в каждом из двух сценариев; результаты между ними не кешируются. Это намеренно не называется одним исполнением на весь suite.

Helpers являются вложенными static-классами, чтобы использовать существующие private UI utilities без копирования и без создания экземпляров тестовых классов. Сами helpers не имеют `[Test]`. Остальные методы исходных тестовых классов не меняют discovery.

После полного validation выявлен upstream NRE в DisposeAsync raw Headless session. В NoMatches helper изменён только factory на уже применяемый `SafeHeadlessUnitTestSession`; assertions и awaited fixture cleanup сохранены. Аналогичная замена двух standalone UI cases и подробное воспроизведение описаны в `ci-test-implementation-evidence.md`. Поэтому финальные семь helper bodies сохраняют assertions, но для NoMatches больше не заявляется полная whitespace-only идентичность исходному body.

`IndependentScenarioCases` собирает независимые assertion failures после безопасного cleanup и продолжает остальные независимые cases. Ошибка host/cleanup или отмена останавливает unsafe continuation, оставшиеся cases получают `not-executed`. Общий BDD остаётся failed. Неизменённые stateful Gherkin steps не продолжаются после разрушенного предусловия.

Новая controlled emoji-регрессия — дополнительный контракт пересечения событий; она не заменяет happy-path coverage.

## Точечные команды

Сначала собрать проект; каждый output directory должен быть новым.

```powershell
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/StormEmojiFilterExecutableSpecTests/*" --report-trx --results-directory artifacts/test-results/emoji-bdd
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/StormSearchBehaviorExecutableSpecTests/*" --report-trx --results-directory artifacts/test-results/search-bdd
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/StormServerStorageCrudRealtimeExecutableSpecTests/*" --report-trx --results-directory artifacts/test-results/server-bdd
```

Во всех трёх командах ожидается один обнаруженный BDD test; внутри него сохраняются названные subcases. Для анализа invocation/outcome включить `UNLIMOTION_TEST_TRACE_DIRECTORY`; счётчик subcases не прибавлять к числу TUnit tests.

Для повторяемых замеров у canonical BDD есть metadata `CiMeasurementPackage=server|emoji|search` (каждое значение отдельно). Фильтр: `/*/*/*/*[CiMeasurementPackage=emoji]`. В архивной baseline тем же значением помечены соответствующий BDD и его прежние самостоятельные дубли; в candidate — только canonical BDD. Это позволяет сравнивать весь пакет за один test process и не приписывать устранению дублей экономию от лишних запусков runner. Метка не меняет обычный full discovery.

В установленном TUnit 1.44.0/MTP 2.2.2 составной OR по именам классов дал zero discovery, хотя такой синтаксис описан в [документации TUnit](https://tunit.dev/docs/execution/test-filters/). Поэтому он не используется для measurement; проверяется фактическое число Passed результатов property-filter: baseline 3/6/3 и candidate 1/1/1 для server/emoji/search.
