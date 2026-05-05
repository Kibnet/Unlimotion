# Гипотезы оптимизации roadmap graph

## 0. Метаданные
- Тип (профиль): guided-artifact-workflow; context `performance-optimization`; stack profile `dotnet-desktop-client`; overlay profile `rendering-pipeline`
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `feat/nodify-graph-control`
- Ограничения:
  - документ фиксирует гипотезы и план замеров, но не меняет код;
  - будущие UI-facing изменения должны сопровождаться UI tests по `AGENTS.override.md`;
  - performance changes должны проходить baseline/post замеры в одинаковой конфигурации;
  - каждую независимую оптимизацию коммитить отдельно.
- Связанные ссылки:
  - `AGENTS.md`
  - `C:\Projects\My\Agents\AGENTS.md`
  - `C:\Projects\My\Agents\instructions\contexts\performance-optimization.md`
  - `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`
  - `C:\Projects\My\Agents\instructions\profiles\rendering-pipeline.md`
  - `src/Unlimotion/Views/GraphControl.axaml`
  - `src/Unlimotion/Views/GraphControl.axaml.cs`
  - `src/Unlimotion/Views/Graph/RoadmapGraphBuilder.cs`
  - `src/Unlimotion/Views/Graph/RoadmapNode.cs`
  - `src/Unlimotion/Views/Graph/RoadmapConnection.cs`
  - `src/Unlimotion.Test/RoadmapGraphUiTests.cs`

## 1. Overview / Цель
Зафиксировать проверяемые предположения, где можно улучшить производительность подготовки и отрисовки карты задач после перехода на Nodify, без ухудшения читаемости roadmap layout.

Outcome contract:
- Success means:
  - есть отдельный структурированный файл с фактами AS-IS, гипотезами, способами замера, рисками и тестами;
  - каждая гипотеза отделяет факт из кода от предположения;
  - есть приоритизация, чтобы начинать не с микрооптимизаций, а с вероятных hot paths;
  - будущий EXEC может взять одну гипотезу и проверить ее benchmark-driven циклом.
- Итоговый артефакт / output: этот документ.
- Stop rules:
  - не менять runtime-код в рамках этой задачи;
  - не утверждать прирост производительности без baseline/post замеров;
  - остановить EXEC будущей оптимизации, если layout quality, UI behavior или automation ids деградируют.

## 2. Текущее состояние (AS-IS)
- `GraphControl.UpdateGraph` выполняется на UI thread и синхронно вызывает `RoadmapGraphBuilder.Build(roots, GetMeasuredRoadmapNodeWidths())`.
- Rebuild graph триггерится из:
  - смены `OnlyUnlocked`, `ShowArchived`, `ShowCompleted`, `ShowWanted`;
  - смены `Tasks`, `UnlockedTasks`, `UpdateGraph`;
  - изменений root collections и filter collections с throttle 100 ms;
  - подписок на свойства и relation collections всех видимых задач;
  - `RoadmapNode_OnSizeChanged`, если измеренная ширина узла изменилась.
- `ScheduleUpdateGraph` коалесцирует только уже поставленный dispatcher callback. Это снижает дубль в рамках одного UI-turn, но не является единым debounce для всех источников rebuild.
- `RoadmapGraphBuilder.Build` каждый раз:
  - обходит дерево задач от roots;
  - создает временные `RoadmapNode`;
  - создает список `ConnectionDefinition`;
  - удаляет избыточные direct paths через `RemoveRedundantConnections`;
  - строит MSAGL graph и запускает Sugiyama layout;
  - поверх результата выполняет собственный goal-anchored layered layout, dummy vertices, barycentric ordering, adjacent swaps, row balancing и row relaxation;
  - возвращает `RoadmapConnection` objects, которые в `ApplyProjection` часто сразу заменяются существующими connection instances и dispose-ятся.
- `ApplyProjection` старается переиспользовать существующие `RoadmapNode` и `RoadmapConnection`, но все равно строит временные nodes/connections, словари и выполняет поэлементные `ObservableCollection` операции.
- `GraphControl.axaml` рендерит логическую связь двумя `nodify:LineConnection` для коротких source nodes: extension без стрелки и основной arrow segment.
- `nodify:Minimap` получает все `RoadmapNodes` и имеет отдельный item template для каждого узла.
- Уже есть UI/projection tests в `RoadmapGraphUiTests`, но нет baseline performance benchmark или счетчиков rebuild/render churn.

## 3. Проблема
Без измеримого performance baseline сложно отличить реальные hot paths от визуально заметных, но дешевых участков. По коду наиболее вероятны задержки UI thread при полной пересборке layout и лишняя работа Avalonia/Nodify при применении projection и отрисовке всех node/edge visuals.

## 4. Цели дизайна
- Измеримость: каждую оптимизацию подтверждать `Mean` latency и `Allocated` allocations.
- UI responsiveness: снизить время блокировки UI thread при rebuild/open/filter/rename flows.
- Layout quality: не ухудшать left-to-right roadmap, минимизацию пересечений и выравнивание outgoing bends.
- Инкрементальность: оптимизировать независимыми пунктами, чтобы откат был дешевым.
- Совместимость: не ломать `RoadmapRoot`, `RoadmapZoomBorder`, node markers, search highlight, drag/drop, double tap, minimap and viewport controls.

## 5. Non-Goals (чего НЕ делаем)
- Не менять storage и domain model задач.
- Не менять пользовательскую семантику связей и фильтров.
- Не отключать minimap/viewport controls без отдельного UX-решения.
- Не ухудшать текущие UI tests ради performance.
- Не принимать оптимизацию только на основании визуального ощущения.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `RoadmapGraphBuilder` -> preparation/layout hot path, algorithmic benchmarks, allocation reduction.
- `GraphControl.axaml.cs` -> scheduling, subscriptions, snapshot/apply projection, UI thread latency.
- `GraphControl.axaml` -> visual tree complexity for nodes, connections and minimap.
- `RoadmapNode` / `RoadmapConnection` -> notification volume and projection object lifetime.
- `RoadmapGraphUiTests` -> UI behavior regression coverage.
- New benchmark/perf harness -> reproducible baseline/post measurements.

### 6.2 Детальный дизайн
- Поток данных для будущих optimizations:
  - UI thread берет immutable snapshot текущих roots/filter/measured widths;
  - heavy graph preparation/layout работает по snapshot;
  - UI thread применяет только последний актуальный projection;
  - visual-only updates не запускают full structural layout.
- Контракты / API:
  - public UI behavior остается прежним;
  - projection может быть разделен на immutable layout DTO and live UI objects, если это уменьшит allocations;
  - graph version/signature должен предотвращать применение устаревших background результатов.
- Output contract / evidence rules:
  - для каждого optimization item фиксировать baseline, change, post measurement, targeted tests, full-run status;
  - item считается успешным только при значимом снижении latency/allocations без падения roadmap UI tests.
- Границы сохранения поведения:
  - directed edges remain left-to-right;
  - redundant short achievement paths remain hidden;
  - outgoing bends remain aligned;
  - filter/search/create/delete/rename flows remain live.
- Обработка ошибок:
  - если async/background layout падает, логировать и сохранять последний рабочий graph или fallback layout;
  - устаревшие результаты layout не применять.
- Производительность:
  - сначала baseline instrumentation;
  - затем оптимизации по приоритету P0/P1/P2 ниже.

## 7. Гипотезы оптимизации

| ID | Приоритет | Hot path | Факт из кода | Гипотеза | Замер | Риски | Тесты |
| --- | --- | --- | --- | --- | --- | --- | --- |
| H0 | P0 | Baseline | Сейчас нет benchmark/perf counters для roadmap graph. | Сначала нужен harness: `RoadmapGraphBuilder.Build` на small/medium/dense fixtures, open-view latency, rebuild count after filters/rename. Без этого нельзя безопасно выбирать оптимизации. | BenchmarkDotNet or dedicated perf test: Mean, P95 if available, Allocated, node/edge/dummy counts, rebuild count. | Некачественный fixture даст ложный hot path. | Existing `RoadmapGraphUiTests`; new perf harness should not be flaky gate by default. |
| H1 | P1 | UI thread blocking | `UpdateGraph` синхронно вызывает full builder на UI thread. | Snapshot + background layout + cancellation of stale builds снизит user-visible freezes on large maps. | UI open/filter latency; time spent on UI thread before/after; dropped stale builds count. | Нельзя перечислять live VM collections off UI thread; нужен immutable snapshot. Возможны race conditions. | UI tests for create/delete/filter/rename/search; projection tests for layout invariants. |
| H2 | P1 | Transitive reduction | `RemoveRedundantConnections` запускает BFS-like `HasAlternativePath` для каждой connection. | Reachability per tail или bitset transitive reduction снизит cost с `E * (V+E)` на dense graphs. | Builder benchmark with many alternative paths; count calls/visited edges. | Нужно сохранить семантику "скрыть direct path только когда есть longer achievement path"; cycles требуют guard. | Existing hidden direct path test plus dense graph tests with cycles/parallel relation kinds. |
| H3 | P1 | Double layout | `ApplySugiyamaLayout` сначала считает MSAGL layout, затем `ApplyRoadmapLocations` пересчитывает own goal-anchored layers/rows. | Можно выбрать один источник layout order: либо MSAGL только как optional ordering seed, либо полностью custom layout без MSAGL. Это может убрать крупный CPU/allocation блок. | Builder benchmark with MSAGL on/off; compare crossings/edge length metrics and screenshots/headless smoke. | Удаление MSAGL может ухудшить сложные dense maps; сохранение только MSAGL может вернуть визуальные дефекты. | Projection crossing tests, open-view screenshot review, roadmap UI smoke. |
| H4 | P1 | Crossing counting | `CountWeightedCrossings` pairwise scans edges; swap phases use affected edges against all edges. | Per-layer-pair inversion counting/Fenwick tree or cached layer buckets снизит O(E^2) участки on dense graphs. | CPU profile inside `OptimizeLayerOrder`, `ApplyAdjacentCrossingSwaps`, `BalanceLayerOrderByNeighborRows`. | Алгоритм сложнее; риск wrong crossing score and worse layout. | Existing crossing tests plus new dense synthetic crossing benchmark. |
| H5 | P1 | Projection allocations | Builder creates temporary `RoadmapNode` and `RoadmapConnection`; `ApplyProjection` reuses existing live objects and disposes projected connections. | Return immutable layout DTOs (`node id`, location, width, connection keys) and create live UI objects only in `ApplyProjection`. Это снизит allocations and event subscription churn. | Allocated bytes/build; Gen0 count; count constructed/disposed `RoadmapConnection`. | Refactor touches projection contract; must preserve bindings and connection notifications. | `RoadmapGraphUiTests/*`, especially rename, create/delete, outgoing route alignment. |
| H6 | P1 | Size measurement rebuild loop | Every node `SizeChanged` can call `ScheduleUpdateGraph` when width changes. | Batch measured-width changes and rebuild once after layout settles, or premeasure text width using Avalonia text metrics so first layout has accurate widths. | Rebuild count after first opening roadmap and after title rename; time to stable layout. | Premeasure can diverge from actual template/theme; batching can delay arrow alignment. | Rename width test, open-view tests, visual check for short/long node arrows. |
| H7 | P1 | Rebuild scheduling | Multiple event sources throttle independently and `ScheduleUpdateGraph` posts immediately once unqueued. | Merge rebuild triggers into one observable pipeline with debounce/throttle, `DistinctUntilChanged` structural signature and cancellation. | Number of `UpdateGraph` calls per user action; filter/rename latency. | Too much debounce makes UI feel stale; missing trigger creates stale graph. | UI tests for filter change, create/delete, rename, relation edits if present. |
| H8 | P2 | Subscription churn | `RegisterRoadmapScopeSubscriptions` rebuilds subscriptions for every projection node on every graph rebuild. | Maintain subscriptions by task id and relation collection identity, diffing added/removed visible tasks. | Subscription count and time in register step on large graph. | Harder lifecycle; leaks or missed updates are possible. | Memory leak smoke if available; UI tests for live property/collection changes. |
| H9 | P2 | ApplyProjection notifications | `SynchronizeRoadmapNodes/Connections` performs individual `Move/Insert/Remove`, each can notify Nodify. | For large reorder diffs, use batched reset/range collection or "replace all when diff is big, incremental when small". | ApplyProjection time; collection change notification count; UI render time after filter. | Reset can recreate visuals and lose viewport/selection; incremental remains best for small edits. | Open/filter/create/delete UI tests; viewport retention check if added. |
| H10 | P2 | Connection visual tree | Each logical edge can render two `LineConnection` controls; all connections stay in visual tree. | Custom lightweight connection renderer or precomputed single path could reduce visual count and layout invalidations. | Visual count, render frame time, memory after open on dense graph. | Higher implementation risk; may bypass Nodify features and break arrows/anchors. | UI visual smoke, arrow direction/projection tests, manual screenshot review. |
| H11 | P2 | Minimap rendering | Minimap renders all nodes via item template and tracks viewport. | Use simplified drawing/bitmap/cache for minimap, or throttle minimap updates on large graphs. | Open time and pan/zoom frame time with minimap visible/hidden. | Minimap usability can degrade; UX decision needed if disabling for large graphs. | Existing minimap controls test plus pan/zoom smoke. |
| H12 | P2 | Search highlight | `UpdateHighlights` enumerates roots and normalizes text for every task on every search change. | Cache normalized searchable text per task and invalidate only on relevant content changes. | Search latency and allocations on large graph; fuzzy search cost separately. | Cache invalidation bugs can produce stale highlight. | Search highlight UI test plus content-change highlight regression. |
| H13 | P2 | Dummy vertex expansion | Long edges create dummy vertices for every intermediate layer. | Compress long-edge representation for scoring, or create dummy vertices only where layer-pair crossings require them. | Dummy vertex count, layout time and allocations on wide graphs. | Dummy vertices improve layout quality; over-compression can reintroduce wandering left paths. | Long-edge and dense-chain projection tests. |
| H14 | P2 | Full graph after small relation edit | Relation collection changes schedule full rebuild. | Incremental update of affected connected component could reduce cost for large independent maps. | Rebuild latency for adding/removing one task/relation in large graph. | Layout global optimum can change beyond component; component isolation must be proven. | Create/delete and relation-edit tests; compare crossing/length metrics. |
| H15 | P3 | Micro allocations | Builder allocates many short-lived dictionaries/lists/LINQ iterators per build. | After algorithmic wins, replace hot LINQ paths with indexed arrays and pooled buffers for dense graph only. | Allocated bytes after H2-H5; profiler allocation call tree. | Lower readability; not worth it before hot path proof. | Projection tests and benchmark only. |

## 8. Точки интеграции и триггеры
- Baseline instrumentation should observe:
  - `GraphControl.UpdateGraph`;
  - `RoadmapGraphBuilder.Build`;
  - `RemoveRedundantConnections`;
  - `ApplySugiyamaLayout`;
  - `ApplyRoadmapLocations`;
  - `OptimizeLayerOrder`;
  - `BalanceLayerOrderByNeighborRows`;
  - `RelaxRows`;
  - `ApplyProjection`;
  - `RoadmapNode_OnSizeChanged`;
  - `UpdateHighlights`.
- User actions to measure:
  - first open of roadmap tab;
  - toggle filters;
  - rename visible task;
  - create/delete visible task;
  - add/remove relation if reachable from UI tests;
  - search typing and clearing;
  - pan/zoom with minimap visible.

## 9. Изменения модели данных / состояния
- Для этого документа: persisted data не меняется.
- Возможные будущие calculated state:
  - immutable graph snapshot;
  - graph structural signature/version;
  - measured-width batch cache;
  - normalized search text cache;
  - layout DTOs separate from live `RoadmapNode`/`RoadmapConnection`;
  - per-task/per-collection subscription registry.

## 10. Миграция / Rollout / Rollback
- Для этого документа: миграция не применима.
- Для будущих оптимизаций:
  - включать одну гипотезу за раз;
  - коммитить отдельно;
  - иметь быстрый rollback путем revert отдельного коммита;
  - если benchmark improves but UI layout degrades, change is rejected.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria для будущей performance work:
  - baseline captured before code change;
  - post measurement captured in same machine/configuration;
  - `Mean` latency and `Allocated` allocations reported;
  - targeted roadmap tests pass;
  - relevant UI tests pass for any UI-facing change;
  - full test run attempted before final report or exact blocker documented.
- Suggested benchmark scenarios:
  - `Build_SmallTree`: 50-100 nodes, sparse edges;
  - `Build_DenseRoadmap`: 300-500 nodes, mixed contains/blocks, long edges;
  - `Build_AlternativePaths`: dense prerequisite paths for transitive reduction;
  - `OpenRoadmap_DenseFixture`: headless open-view latency and stable rebuild count;
  - `RenameTitle_DenseFixture`: number of rebuilds until stable width;
  - `Search_DenseFixture`: highlight latency.
- Commands for future verification:
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=artifacts\codex-roadmap-perf-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"`
  - `dotnet build src\Unlimotion\Unlimotion.csproj --nologo -v:minimal -p:BaseOutputPath=artifacts\codex-roadmap-perf-build\ -p:UseSharedCompilation=false`
  - `dotnet run --project <benchmark-project> -c Release -- --filter *RoadmapGraph*`
  - `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -p:BaseOutputPath=artifacts\codex-roadmap-perf-headless-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowHeadlessTests/Major_tabs_can_be_opened_from_main_window"`
- Stop rules для validation loops:
  - не продолжать оптимизацию без воспроизводимого baseline;
  - если targeted roadmap UI tests падают, сначала исправить regression или откатить item;
  - если benchmark шумит, повторить до устойчивого тренда или считать результат недоказанным;
  - если улучшение только allocation/CPU micro-level и ухудшает readability, откатить.

## 12. Риски и edge cases
- Large graph can be CPU-bound in builder and render-bound in Nodify at the same time; нужна раздельная телеметрия.
- Background layout can introduce stale projection races.
- Reducing dummy vertices or MSAGL phases can reintroduce visual defects already fixed by previous roadmap iterations.
- Batched collection reset can recreate controls and disturb viewport/minimap behavior.
- Caching normalized search text or graph signatures can go stale after title/emoji/repeater/relation changes.
- Perf tests can become flaky if tied to wall-clock UI timings; benchmark thresholds should be used as evidence, not brittle CI gate, unless stabilized.

## 13. План выполнения
1. Add baseline instrumentation/benchmark harness (H0).
2. Measure current `Build` CPU/allocations and `UpdateGraph` UI latency on dense fixtures.
3. Pick first optimization from P1 based on measured hot path.
4. Implement exactly one item.
5. Run targeted tests and post benchmark.
6. Keep change only if improvement is significant and behavior tests pass.
7. Repeat with the next highest measured hot path.

## 14. Открытые вопросы
- Какие реальные размеры пользовательских графов считать целевыми для benchmark: 100, 500, 1000+ nodes?
- Нужен ли hard budget для first open/filter action, например `< 100 ms` UI thread block?
- Можно ли для очень больших графов деградировать minimap fidelity ради интерактивности, или minimap должен оставаться точным?

## 15. Соответствие профилю
- Профиль: `performance-optimization`, `dotnet-desktop-client`, `rendering-pipeline`
- Выполненные требования профиля:
  - для каждого optimization item указаны гипотеза, способ замера, риски и тесты;
  - AS-IS pipeline and invalidation triggers described;
  - UI thread blocking risk explicitly called out;
  - future UI-facing changes require UI tests per local override;
  - baseline/post measurement required before accepting changes.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-05-05-roadmap-graph-performance-hypotheses.md` | Новый документ с гипотезами performance optimization | Зафиксировать структурированный backlog замеров и оптимизаций |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Performance backlog | Рассеянные предположения в ходе UI итераций | Приоритизированный список проверяемых гипотез |
| Evidence contract | Не задан для performance | Baseline/post measurements, tests and rollback rules |
| Render pipeline | Описан в коде, но не как perf model | Зафиксированы rebuild/render triggers and hot paths |

## 18. Альтернативы и компромиссы
- Вариант: сразу оптимизировать очевидные участки без benchmark.
  - Плюсы: быстрее начать кодить.
  - Минусы: высокий риск оптимизировать не hot path и ухудшить layout supportability.
  - Почему не выбран: центральный `performance-optimization` требует baseline/post evidence.
- Вариант: переписать layout целиком.
  - Плюсы: потенциально убрать накопленную сложность.
  - Минусы: высокий риск регрессий roadmap readability.
  - Почему не выбран: текущие визуальные требования уже сложные; безопаснее оптимизировать измеренные участки по одному.
- Вариант: отключить minimap or complex edges for large graphs.
  - Плюсы: может быстро снизить render cost.
  - Минусы: меняет UX and feature parity.
  - Почему не выбран: требует отдельного продуктового решения, пока только гипотеза H11.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals and non-goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Есть pipeline model, integration triggers, state and rollback rules. |
| C. Безопасность изменений | 11-13 | PASS | Документ не меняет код; будущие changes gated by tests/rollback. |
| D. Проверяемость | 14-16 | PASS | Для каждой гипотезы есть measurement/test evidence. |
| E. Готовность к автономной реализации | 17-19 | PASS | P0/P1/P2 priority and step plan defined; open questions non-blocking for baseline harness. |
| F. Соответствие профилю | 20 | PASS | Performance, desktop and rendering profile requirements covered. |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Документ ограничен гипотезами и планом замеров. |
| 2. Понимание текущего состояния | 5 | AS-IS привязан к конкретным классам/methods/triggers. |
| 3. Конкретность целевого дизайна | 5 | Есть snapshot/apply pipeline and prioritized hypotheses. |
| 4. Безопасность (миграция, откат) | 5 | One-item commits, rollback and rejection criteria defined. |
| 5. Тестируемость | 5 | Measurement, targeted UI/projection tests and full-run expectations defined. |
| 6. Готовность к автономной реализации | 5 | H0 gives first executable step; P1/P2 order is clear. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: гипотезы разделены на fact/hypothesis/measurement/risk/tests; добавлены stop rules and rejection criteria.
- Что осталось на решение пользователя: выбрать performance budgets and whether minimap fidelity may degrade on very large graphs.

## Approval
Для реализации любой отдельной оптимизации ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Собрать central instruction stack | 0.95 | Нет | Исследовать graph hot paths | Нет | Нет | Локальный `AGENTS.md` требует центральный каталог; routing выбрал performance + desktop + rendering | `AGENTS.md`, central instructions |
| SPEC | Исследовать подготовку и отрисовку graph | 0.86 | Нет baseline замеров | Записать гипотезы | Нет | Нет | Код показывает потенциальные hot paths в full rebuild, layout algorithms, projection apply and Nodify visuals | `GraphControl.axaml.cs`, `GraphControl.axaml`, `RoadmapGraphBuilder.cs`, `RoadmapNode.cs`, `RoadmapConnection.cs` |
| SPEC | Создать performance hypotheses artifact | 0.88 | Реальные целевые размеры графов и latency budget | Ожидать выбора/подтверждения EXEC | Да, для будущей реализации | Нет | Пользователь попросил отдельный файл с предположениями, не runtime change | `specs/2026-05-05-roadmap-graph-performance-hypotheses.md` |
| EXEC | H0: добавить baseline perf harness | 0.88 | Нет целевых production budgets | Проверить первую P1-гипотезу по hot path | Нет | Да, пользователь подтвердил spec | Console harness измеряет Mean/Allocated для synthetic roadmap scenarios; baseline: `small-chain` 788.57 ms / 31.7 MB, `dense-shortcuts` 1646.43 ms / 92.0 MB, `tree-with-blocks` 495.08 ms / 111.1 MB | `tests/Unlimotion.Performance/*`, `src/Unlimotion.sln` |
| EXEC | H2: hybrid redundant-path pruning | 0.86 | Нужны production графы и стабильный full-run perf budget | Закоммитить H2 и перейти к следующей P1-гипотезе | Нет | Да, пользователь подтвердил spec | Baseline H0: `small-chain` 788.57 ms / 32452.7 KB, `dense-shortcuts` 1646.43 ms / 94167.0 KB, `tree-with-blocks` 495.08 ms / 113774.1 KB. Post H2: `small-chain` 490.86 ms / 32584.0 KB, `dense-shortcuts` 1190.56 ms / 94663.3 KB, `tree-with-blocks` 342.40 ms / 114262.8 KB. Результат: latency лучше на 37.8%, 27.7%, 30.8%; allocations примерно без улучшения. Roadmap UI tests: 20/20 PASS. | `src/Unlimotion/Views/Graph/RoadmapGraphBuilder.cs`, `specs/2026-05-05-roadmap-graph-performance-hypotheses.md` |
| EXEC | H4: layer-pair crossing buckets | 0.62 | Нужен CPU profiler, чтобы понять, почему bucketed scoring шумит и иногда регрессирует | Не коммитить H4; перейти к другой гипотезе | Нет | Да, пользователь подтвердил spec | Baseline H2 head: `small-chain` 921.60 ms / 32555.2 KB, `dense-shortcuts` 1774.21 ms / 94886.8 KB, `tree-with-blocks` 541.68 ms / 114261.1 KB. Post attempts были нестабильны: 652.54/1716.48/644.29 ms, затем 656.19/786.90/485.64 ms, затем 1054.24/1862.03/926.94 ms. Результат отклонен: repeated post не подтвердил устойчивое улучшение, `tree-with-blocks` ухудшался, roadmap UI tests дважды упирались в timeout при параллельной нагрузке. | `src/Unlimotion/Views/Graph/RoadmapGraphBuilder.cs` (reverted) |
| EXEC | H5: projected connections without node subscriptions | 0.72 | Нужен более глубокий allocation profile перед refactor DTO projection | Не коммитить H5; сохранить только rejected evidence | Нет | Да, пользователь подтвердил spec | Baseline H2 head: `small-chain` 1146.68 ms / 31351.2 KB, `dense-shortcuts` 1540.75 ms / 90096.3 KB, `tree-with-blocks` 716.24 ms / 114282.4 KB. Post H5: `small-chain` 1286.77 ms / 32426.2 KB, `dense-shortcuts` 3001.25 ms / 94868.1 KB, `tree-with-blocks` 775.49 ms / 114259.7 KB. Результат отклонен: latency хуже, allocation benefit отсутствует. | `src/Unlimotion/Views/Graph/RoadmapConnection.cs`, `src/Unlimotion/Views/Graph/RoadmapGraphBuilder.cs` (reverted) |
| EXEC | H15: connection key micro-allocation | 0.68 | Нужен profiler allocation call tree перед дальнейшими micro changes | Не коммитить H15; перейти к более крупным hot paths | Нет | Да, пользователь подтвердил spec | Baseline H2 head: `small-chain` 916.70 ms / 32554.5 KB, `dense-shortcuts` 1660.85 ms / 96055.8 KB, `tree-with-blocks` 446.78 ms / 114261.1 KB. Post H15 struct-key: `small-chain` 1569.82 ms / 32414.9 KB, `dense-shortcuts` 2613.66 ms / 94149.3 KB, `tree-with-blocks` 1280.45 ms / 113818.6 KB. Результат отклонен: micro allocation saving не окупает latency regression. | `src/Unlimotion/Views/Graph/RoadmapGraphBuilder.cs` (reverted) |
| EXEC | H3: remove or reduce MSAGL seed | 0.74 | Нужна layout-quality screenshot review before any future MSAGL removal | Не коммитить H3; не отключать MSAGL без отдельного профилирования | Нет | Да, пользователь подтвердил spec | Baseline H2 head: `small-chain` 916.70 ms / 32554.5 KB, `dense-shortcuts` 1660.85 ms / 96055.8 KB, `tree-with-blocks` 446.78 ms / 114261.1 KB. No-MSAGL post: `small-chain` 742.70 ms / 10989.0 KB, `dense-shortcuts` 2089.03 ms / 32905.6 KB, `tree-with-blocks` 618.55 ms / 105774.7 KB; roadmap UI tests 20/20 PASS. Light-MSAGL post: `small-chain` 1439.02 ms / 17166.9 KB, `dense-shortcuts` 4092.78 ms / 54672.1 KB, `tree-with-blocks` 1114.16 ms / 114339.5 KB. Результат отклонен: allocations improve, но latency хуже на важных сценариях. | `src/Unlimotion/Views/Graph/RoadmapGraphBuilder.cs` (reverted) |

## 21. Результаты EXEC-замеров
| ID | Change | Baseline command | Baseline Mean / Allocated | Post command | Post Mean / Allocated | Результат | Проверка |
| --- | --- | --- | --- | --- | --- | --- | --- |
| H2 | Hybrid cache для поиска альтернативных достижимых путей в `RemoveRedundantConnections`; cache включается только для tails с 5+ outgoing edges, для малых tails оставлен early-exit BFS | `dotnet run --project tests\Unlimotion.Performance\Unlimotion.Performance.csproj -c Release -- --warmup 1 --iterations 3` | `small-chain`: 788.57 ms / 32452.7 KB; `dense-shortcuts`: 1646.43 ms / 94167.0 KB; `tree-with-blocks`: 495.08 ms / 113774.1 KB | `dotnet run --no-build --project tests\Unlimotion.Performance\Unlimotion.Performance.csproj -c Release -- --warmup 1 --iterations 5` | `small-chain`: 490.86 ms / 32584.0 KB; `dense-shortcuts`: 1190.56 ms / 94663.3 KB; `tree-with-blocks`: 342.40 ms / 114262.8 KB | Mean latency: -37.8%, -27.7%, -30.8%; allocations: +0.4%, +0.5%, +0.4%, поэтому H2 принят как CPU/latency optimization, не как allocation optimization | `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=artifacts\codex-roadmap-h2-final-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"` -> 20/20 PASS |
| H4 | Bucketed crossing scoring по layer-pair для `CountWeightedCrossings` and `CountAffectedCrossings` | `dotnet run --no-build --project tests\Unlimotion.Performance\Unlimotion.Performance.csproj -c Release -- --warmup 1 --iterations 5` | `small-chain`: 921.60 ms / 32555.2 KB; `dense-shortcuts`: 1774.21 ms / 94886.8 KB; `tree-with-blocks`: 541.68 ms / 114261.1 KB | Three post attempts: build run iterations 5, no-build iterations 3, no-build iterations 5 | Post attempts: `small-chain`: 652.54 -> 656.19 -> 1054.24 ms; `dense-shortcuts`: 1716.48 -> 786.90 -> 1862.03 ms; `tree-with-blocks`: 644.29 -> 485.64 -> 926.94 ms | Rejected: эффект нестабилен, финальный повтор хуже baseline на всех сценариях; no code committed | Roadmap UI tests timed out when run in parallel with perf, so H4 was not accepted |
| H5 | Projection `RoadmapConnection` instances without node `PropertyChanged` subscriptions | `dotnet run --no-build --project tests\Unlimotion.Performance\Unlimotion.Performance.csproj -c Release -- --warmup 1 --iterations 3` | `small-chain`: 1146.68 ms / 31351.2 KB; `dense-shortcuts`: 1540.75 ms / 90096.3 KB; `tree-with-blocks`: 716.24 ms / 114282.4 KB | `tests\Unlimotion.Performance\artifacts\codex-h5-build\Release\net10.0\Unlimotion.Performance.exe --warmup 1 --iterations 3` | `small-chain`: 1286.77 ms / 32426.2 KB; `dense-shortcuts`: 3001.25 ms / 94868.1 KB; `tree-with-blocks`: 775.49 ms / 114259.7 KB | Rejected: latency worse and allocations not improved; no code committed | Build passed with existing warnings; perf result rejected before UI test gate |
| H15 | Replace string-formatted connection dedupe keys with struct keys | `tests\Unlimotion.Performance\artifacts\codex-h15-baseline-build\Release\net10.0\Unlimotion.Performance.exe --warmup 1 --iterations 3` | `small-chain`: 916.70 ms / 32554.5 KB; `dense-shortcuts`: 1660.85 ms / 96055.8 KB; `tree-with-blocks`: 446.78 ms / 114261.1 KB | `tests\Unlimotion.Performance\artifacts\codex-h15-post-build\Release\net10.0\Unlimotion.Performance.exe --warmup 1 --iterations 3` | `small-chain`: 1569.82 ms / 32414.9 KB; `dense-shortcuts`: 2613.66 ms / 94149.3 KB; `tree-with-blocks`: 1280.45 ms / 113818.6 KB | Rejected: small allocation drop with material latency regression; no code committed | Build passed with existing warnings |
| H3 | Skip MSAGL initial layout or reduce MSAGL ordering passes | Same H15 baseline executable | `small-chain`: 916.70 ms / 32554.5 KB; `dense-shortcuts`: 1660.85 ms / 96055.8 KB; `tree-with-blocks`: 446.78 ms / 114261.1 KB | No-MSAGL executable and light-MSAGL executable, both `--warmup 1 --iterations 3` | No-MSAGL: `small-chain`: 742.70 ms / 10989.0 KB; `dense-shortcuts`: 2089.03 ms / 32905.6 KB; `tree-with-blocks`: 618.55 ms / 105774.7 KB. Light-MSAGL: `small-chain`: 1439.02 ms / 17166.9 KB; `dense-shortcuts`: 4092.78 ms / 54672.1 KB; `tree-with-blocks`: 1114.16 ms / 114339.5 KB | Rejected: memory improves, but latency regresses on dense/tree scenarios; no code committed | No-MSAGL roadmap UI tests: 20/20 PASS |
