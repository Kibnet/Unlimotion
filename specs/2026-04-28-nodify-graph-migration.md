# Замена roadmap graph на Nodify

## 0. Метаданные
- Тип (профиль): delivery-task; stack profile `dotnet-desktop-client`; overlay profile `ui-automation-testing`
- Владелец: Codex
- Масштаб: large
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: фаза SPEC меняет только этот файл; EXEC начинается только после фразы `Спеку подтверждаю`; UI-facing change MUST сопровождаться UI tests; сохранить `automation-id` для roadmap flow
- Связанные ссылки:
  - `AGENTS.md`, `AGENTS.override.md`
  - `src/Unlimotion/Views/GraphControl.axaml`
  - `src/Unlimotion/Views/GraphControl.axaml.cs`
  - `src/Unlimotion.ViewModel/GraphViewModel.cs`
  - `src/Directory.Packages.props`
  - `src/Unlimotion/Unlimotion.csproj`
  - NodifyAvalonia: https://www.nuget.org/packages/NodifyAvalonia
  - Nodify Avalonia repo: https://github.com/BAndysc/nodify-avalonia

## 1. Overview / Цель
Заменить текущий roadmap graph на `NodifyAvalonia`, убрав зависимость от `AvaloniaGraphControl`, без потери пользовательского поведения карты задач.

Outcome contract:
- Success means:
  - roadmap tab рендерит задачи и связи через `Nodify.NodifyEditor`;
  - сохранены фильтры, поиск/подсветка, completion checkbox, wanted bold, repeater marker, double-tap details toggle, drag source format, drop integration с `MainControl`, zoom/pan usability и automation ids;
  - зеленые parent-child связи и красные block связи остаются различимыми и направленными;
  - dependency switch отражен в central package management;
  - добавлены/обновлены релевантные UI tests и они проходят.
- Итоговый артефакт / output: измененный код, тесты, успешные targeted UI tests, build, полный доступный test run или явная причина, если полный прогон невозможен.
- Stop rules:
  - остановиться до EXEC, если пользователь не подтвердил spec фразой `Спеку подтверждаю`;
  - на EXEC остановиться и запросить решение, если Nodify не позволяет сохранить важный пользовательский сценарий без продуктового tradeoff;
  - не завершать EXEC при падающих targeted UI tests, связанных с roadmap graph.

## 2. Текущее состояние (AS-IS)
- `GraphControl.axaml` использует `AvaloniaGraphControl.GraphPanel` внутри `ZoomBorder` из `Avalonia.Controls.PanAndZoom`.
- `GraphControl.axaml.cs` строит `AvaloniaGraphControl.Graph` в code-behind, задает `Orientation = Horizontal` и `LayoutMethod = SugiyamaScheme`.
- Связи:
  - `ContainEdge` наследует `AvaloniaGraphControl.Edge`, рисуется зеленой `Connection`;
  - `BlockEdge` наследует `AvaloniaGraphControl.Edge`, рисуется красной `Connection`;
  - одиночные задачи без связей добавляются через self-edge.
- Узел задачи в graph template содержит `CheckBox IsCompleted`, emoji, repeater marker, `TitleWithoutEmoji`, классы `highlighted`, `IsWanted`, `IsCanBeCompleted`, pointer drag и double tap.
- `GraphViewModel` хранит root collections (`Tasks`, `UnlockedTasks`), фильтры и `Search`.
- `MainControl` принимает drag/drop данные как `MainControl.CustomFormat` или `GraphControl.CustomFormat`.
- Существующие проверки:
  - `TaskImportanceUiTests.WantedTaskTitle_ShouldBeBold_InRoadmapGraph`;
  - structural check `TaskRepeaterMarker_GraphTemplate_AddsMarkerBeforeTitle`;
  - AppAutomation page objects ожидают `RoadmapRoot` и `RoadmapZoomBorder`.
- Скрытая зависимость: Nodify не является drop-in replacement для `GraphPanel`; он ожидает node positions. Старый layout делал `SugiyamaScheme` автоматически.

## 3. Проблема
Текущая карта задач привязана к `AvaloniaGraphControl`, а замена на Nodify требует сохранить весь UI-контракт графа, включая автоматическую раскладку и interaction flows, которые сейчас частично реализованы самим старым компонентом.

## 4. Цели дизайна
- Разделение ответственности: projection/layout отделить от XAML и от raw `TaskWrapperViewModel` traversal.
- Повторное использование: использовать существующие `TaskItemViewModel` bindings для содержимого узла.
- Тестируемость: вынести построение roadmap model/layout в тестируемый код без зависимости от visual tree.
- Консистентность: сохранить внешний вид node content и automation ids, чтобы не ломать UI tests/readme tooling.
- Обратная совместимость: не менять storage, task VM API и public app flows.

## 5. Non-Goals (чего НЕ делаем)
- Не добавлять редактирование связей через Nodify connectors.
- Не сохранять пользовательские позиции узлов между запусками.
- Не менять алгоритмы фильтрации задач, поиска, completion, wanted или repeater.
- Не переписывать `MainControl` drag/drop flow за пределами совместимости с graph source.
- Не обновлять Avalonia, TUnit или unrelated packages.
- Не менять README/media, кроме случая, если существующие tests требуют structural selector update.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Directory.Packages.props` -> заменить `AvaloniaGraphControl` package version на `NodifyAvalonia 6.6.0`.
- `src/Unlimotion/Unlimotion.csproj` -> заменить `PackageReference Include="AvaloniaGraphControl"` на `NodifyAvalonia`.
- `src/Unlimotion/App.axaml` -> подключить Nodify theme через `StyleInclude` или `ResourceInclude` по documented path `avares://Nodify/Theme.axaml`.
- `src/Unlimotion/Views/GraphControl.axaml` -> заменить `GraphPanel` на `nodify:NodifyEditor`, сохранить `RoadmapRoot` и `RoadmapZoomBorder` automation ids.
- `src/Unlimotion/Views/GraphControl.axaml.cs` -> перейти с `AvaloniaGraphControl.Graph` на projection collection для Nodify; сохранить subscriptions, highlight update, drag source и double tap.
- `src/Unlimotion/Views/Graph/*` -> заменить старые edge classes на roadmap projection classes (`RoadmapNode`, `RoadmapConnector`, `RoadmapConnection`, `RoadmapConnectionKind`) или удалить устаревшие классы, если они больше не нужны.
- `src/Unlimotion.Test/*` -> добавить/обновить UI and characterization tests для graph parity.

### 6.2 Детальный дизайн
- Поток данных:
  - `GraphControl` реагирует на те же `GraphViewModel` изменения;
  - `BuildFromTasks` строит in-memory roadmap projection: уникальные nodes по `TaskItemViewModel.Id`, typed connections, calculated positions;
  - `NodifyEditor.ItemsSource` bind/update получает nodes, `Connections` получает connections.
- Контракты / API:
  - public `GraphViewModel` не менять;
  - `GraphControl.CustomFormat` сохранить;
  - `RoadmapRoot` сохранить на root `UserControl`;
  - `RoadmapZoomBorder` сохранить на `NodifyEditor` или immediate wrapper, чтобы AppAutomation selectors не деградировали;
  - node visual `DataContext` для title TextBlock должен оставаться `TaskItemViewModel` или тесты должны иметь стабильный эквивалент без позиционных selectors.
- Layout:
  - реализовать deterministic horizontal layered layout вместо `SugiyamaScheme`;
  - depth считать по contain edges от roots, block edges не должны переносить узлы в parent-child слой;
  - within-layer order сохранять стабильным по traversal/order коллекций, fallback по title/id;
  - node spacing использовать константы, достаточные для `MaxWidth=300`;
  - isolated tasks размещать в отдельном layer/row без self-edge.
- Connections:
  - parent-child connection -> green `nodify:LineConnection`;
  - block connection -> red `nodify:LineConnection`;
  - arrow direction сохранить по старому построению `Edge(tail, head)`;
  - source/target anchors брать через Nodify connectors (`Anchor`) либо через calculated points, если connector anchors окажутся нестабильны в headless.
- Node template:
  - сохранить checkbox binding `IsCompleted`/`IsCanBeCompleted`;
  - сохранить emoji, repeater marker (`TaskRepeaterGraphMarker`), `TitleWithoutEmoji`, `IsHighlighted`, `Wanted`, `!IsCanBeCompleted`;
  - сохранить pointer pressed drag setup and double tap details toggle.
- Zoom/pan:
  - использовать встроенные Nodify pan/zoom;
  - клавиши `F`, `U`, `T` должны приводить к fit-to-screen/all-nodes behavior; `R` должен сбрасывать viewport zoom/location;
  - если точная семантика `ZoomBorder.Uniform/ToggleStretchMode` отсутствует в Nodify, это фиксируется как совместимый mapping, потому что Nodify viewport всегда uniform-scale.
- Обработка ошибок:
  - null/empty collections -> empty graph without exception;
  - duplicate task references -> один node, несколько connections;
  - cycles/deadlocks -> traversal guarded by visited set.
- Производительность:
  - сохранять throttling `100 ms`;
  - layout O(V + E) или близко к этому;
  - не делать synchronous storage/network work на UI thread.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Root selection:
  - если `OnlyUnlocked = true`, graph строится из `UnlockedTasks`;
  - иначе из `Tasks`.
- Contain edges:
  - для каждой `task.SubTasks` сохранить текущие исключения:
    - не добавлять contain edge, если child blocks another child;
    - не добавлять contain edge, если child blocks a blocker of parent.
- Block edges:
  - для каждого `task.TaskItem.BlocksTasks` добавить red directed connection from current task to blocked task по текущему contract.
- Linked nodes:
  - любой task, участвующий в connection, должен быть представлен node.
- Isolated nodes:
  - task без связей должен отображаться node без self-loop.
- Search highlight:
  - normalized/fuzzy matching остается как сейчас в `UpdateHighlights`.

## 8. Точки интеграции и триггеры
- `DataContextChanged` -> subscriptions and initial graph rebuild.
- `OnlyUnlocked`, `ShowArchived`, `ShowCompleted`, `ShowWanted` -> rebuild graph.
- `UnlockedTasks` / `Tasks` collection changes -> throttled rebuild.
- `UpdateGraph` flag -> rebuild graph.
- `Search.SearchText` -> throttled highlight update.
- Node pointer pressed -> set `TaskItemViewModel.MainWindowInstance.CurrentTaskItem` and start drag with `GraphControl.CustomFormat`.
- Node double tap -> toggle `DetailsAreOpen`.
- `DragDrop.DropEvent` / `DragDrop.DragOverEvent` -> same `MainControl` handlers.
- KeyDown on graph/editor -> fit/reset handlers.

## 9. Изменения модели данных / состояния
- Persisted data: нет изменений.
- New calculated state:
  - roadmap node collection;
  - roadmap connection collection;
  - node `Location`;
  - connector anchor holders for Nodify.
- Existing state:
  - `GraphViewModel` public properties remain unchanged.

## 10. Миграция / Rollout / Rollback
- First run: no persisted migration.
- Rollout: dependency switch in csproj/central props plus XAML/code-behind change.
- Rollback:
  - revert package refs to `AvaloniaGraphControl`;
  - restore old `GraphPanel` XAML and old edge classes;
  - remove Nodify theme include and projection classes.
- Compatibility check: AppAutomation selectors `RoadmapRoot` and `RoadmapZoomBorder` remain available.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Roadmap tab opens in headless/AppAutomation and exposes `RoadmapRoot` and `RoadmapZoomBorder`.
  - Graph renders at least known fixture tasks from `MainWindowViewModelFixture`.
  - Parent-child and block relationships produce distinct green/red directed connection visuals or projection objects covered by tests.
  - Wanted task title is bold in roadmap graph.
  - Repeater marker remains before `TitleWithoutEmoji` and has `TaskRepeaterGraphMarker`.
  - Search text highlights matching roadmap nodes and clears highlight when search is cleared.
  - Completion checkbox remains bound and disabled/enabled by `IsCanBeCompleted`.
  - Double tap on graph node toggles details panel.
  - Pointer drag from graph node keeps `GraphControl.CustomFormat` path accepted by `MainControl`.
  - Zoom/pan key handlers do not throw and update viewport/fit behavior.
  - No `AvaloniaGraphControl` reference remains in `Unlimotion` project after migration.
- Tests to add/update:
  - Characterization/unit test for roadmap projection counts/kinds on fixture data or small in-memory wrappers.
  - Avalonia.Headless UI test for roadmap render with Nodify nodes and wanted/repeater markers.
  - Avalonia.Headless UI test for search highlight in roadmap.
  - Avalonia.Headless UI test for double-tap details toggle or direct routed event if headless pointer double tap is stable.
  - Update structural test that currently searches old `GraphControl.axaml` strings, so it validates Nodify node template instead of `GraphPanel`.
- Commands for verification:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskImportanceUiTests/*"`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskListRepeaterMarkerUiTests/*"`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/<NewRoadmapGraphTestClass>/*"`
  - `dotnet build src/Unlimotion/Unlimotion.csproj`
  - `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `dotnet test src/Unlimotion.sln` or documented full-run equivalent; if it fails due known solution-wide unrelated issue, report exact failure and completed next-best checks.
- Stop rules for validation loops:
  - do not continue broad refactors after targeted graph tests pass unless build/test shows graph-related failure;
  - if Nodify headless rendering requires extra dispatcher/layout cycles, add deterministic wait helper instead of sleeps where possible;
  - do not finish while targeted roadmap UI tests fail.

## 12. Риски и edge cases
- Risk: Nodify package targets Avalonia 11.1.0 dependency while app uses Avalonia 11.3.7. Mitigation: build and run UI tests; pin latest `NodifyAvalonia 6.6.0` unless restore/build proves incompatibility.
- Risk: Losing automatic Sugiyama quality. Mitigation: deterministic layered layout and tests on relation counts/visibility; keep layout constants conservative.
- Risk: Nodify connectors show visible ports not present before. Mitigation: style connector markers minimally/transparent if needed while preserving anchors.
- Risk: `Node` movement could let users drag roadmap nodes, changing generated layout temporarily. Mitigation: set `ItemContainer.IsDraggable=false` unless product wants editable positions.
- Risk: Old ZoomBorder keyboard semantics have no exact Nodify equivalent. Mitigation: preserve practical fit/reset behavior and automation id; document mapping.
- Risk: Headless tests may not materialize connection anchors immediately. Mitigation: pump dispatcher/layout and test projection model where visual anchor verification is brittle.
- Risk: Cycles/deadlocks in tasks. Mitigation: visited set already exists; preserve it and add projection test if cheap.

## 13. План выполнения
1. Add roadmap projection model/layout behind `GraphControl` or in `Views/Graph`.
2. Add/update tests that characterize current graph output and UI-visible roadmap node behavior.
3. Replace package refs and include Nodify resources.
4. Replace `GraphControl.axaml` with `NodifyEditor` templates for nodes and connections.
5. Update `GraphControl.axaml.cs` to build projection collections, key handlers and drag/double-tap bindings.
6. Remove obsolete `AvaloniaGraphControl` edge classes/usings after compile confirms no remaining references.
7. Run targeted UI tests; fix graph-specific failures.
8. Run build and broader/full tests; record any unrelated blockers.
9. Run post-EXEC review and repeat affected checks if fixes are needed.

## 14. Открытые вопросы
Нет блокирующих вопросов. Предлагаемый mapping для old zoom keys: `F/U/T` -> fit all visible nodes, `R` -> reset viewport.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - UI thread не должен выполнять долгую синхронную работу; layout only in-memory and throttled.
  - UI/integration tests обязательны и перечислены.
  - `automation-id` selectors сохраняются.
  - Verification plan включает `dotnet build`, targeted TUnit/Avalonia.Headless UI tests и full test run.
  - UI tests используют stable automation ids and VM data context, not text/position-only selectors.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Directory.Packages.props` | Replace `AvaloniaGraphControl` version with `NodifyAvalonia` | Central package management |
| `src/Unlimotion/Unlimotion.csproj` | Replace package reference | Dependency switch |
| `src/Unlimotion/App.axaml` | Include Nodify theme | Required Nodify styles |
| `src/Unlimotion/Views/GraphControl.axaml` | Replace `GraphPanel` with `NodifyEditor`; update templates | New graph component |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Build Nodify projection and preserve interactions | Behavior parity |
| `src/Unlimotion/Views/Graph/*.cs` | Replace/remove old edge classes, add projection classes | Remove old component coupling |
| `src/Unlimotion.Test/TaskImportanceUiTests.cs` | Update graph lookup if needed | Preserve wanted bold UI coverage |
| `src/Unlimotion.Test/TaskListRepeaterMarkerUiTests.cs` | Update structural test for Nodify template | Preserve repeater marker coverage |
| `src/Unlimotion.Test/RoadmapGraphUiTests.cs` | Add projection/search/interaction tests | Cover migration risk |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Graph component | `AvaloniaGraphControl.GraphPanel` | `Nodify.NodifyEditor` |
| Layout | Built-in `SugiyamaScheme` | Deterministic horizontal layered layout |
| Pan/zoom | `ZoomBorder` around graph | Nodify viewport pan/zoom with preserved automation id |
| Node content | DataTemplate for `TaskItemViewModel` | Nodify item template containing same task content |
| Connections | `ContainEdge`/`BlockEdge` + `Connection` templates | `RoadmapConnection` + `LineConnection` templates |
| Isolated nodes | self-edge workaround | node without self-loop |
| Tests | partial UI/structural graph checks | graph parity UI + projection checks |

## 18. Альтернативы и компромиссы
- Вариант: Wrap Nodify but keep `AvaloniaGraphControl` for layout calculation.
  - Плюсы: closer old layout.
  - Минусы: dependency not actually replaced; two graph components remain.
  - Почему не выбран: нарушает цель заменить компонент.
- Вариант: Add separate graph layout package.
  - Плюсы: better automatic layout.
  - Минусы: extra dependency and integration risk; larger blast radius.
  - Почему не выбран: current graph is modest and deterministic layered layout is enough for parity.
- Вариант: Hand-render graph with Canvas.
  - Плюсы: maximum control.
  - Минусы: not Nodify; more custom interaction code.
  - Почему не выбран: user explicitly requested Nodify.
- Выбранный вариант: Nodify editor + local projection/layout.
  - Плюсы: satisfies dependency goal, keeps MVVM-friendly node editor, preserves testable behavior.
  - Минусы: layout responsibility moves into app code.
  - Почему выбранное решение лучше в контексте этой задачи: it removes old component while limiting behavior changes to a controlled projection layer.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заданы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, layout, integrations, state and rollback covered |
| C. Безопасность изменений | 11-13 | PASS | Test plan, risks and staged plan included |
| D. Проверяемость | 14-16 | PASS | Blocking questions absent; commands and file table included |
| E. Готовность к автономной реализации | 17-19 | PASS | Parity table, alternatives, review result included |
| F. Соответствие профилю | 20 | PASS | UI tests, stable selectors, build/full-run requirements captured |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Replace component with explicit Non-Goals and preserved behaviors |
| 2. Понимание текущего состояния | 5 | Current graph code, dependencies and tests identified |
| 3. Конкретность целевого дизайна | 5 | Nodify projection, layout, templates and key mappings defined |
| 4. Безопасность (миграция, откат) | 5 | No persisted migration; rollback steps defined |
| 5. Тестируемость | 5 | UI and projection tests plus commands listed |
| 6. Готовность к автономной реализации | 5 | No blocking questions; staged plan and risk mitigations present |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: в spec добавлены explicit parity list, Nodify dependency choice, layout risk, old zoom key mapping, AppAutomation selector preservation, no-self-loop rule for isolated nodes.
- Что осталось на решение пользователя: только переход в EXEC фразой `Спеку подтверждаю`.

### EXEC Result
- Статус: ГОТОВО
- Реализация:
  - dependency заменена с `AvaloniaGraphControl` на `NodifyAvalonia 6.6.0`;
  - Nodify theme подключена через `avares://Nodify/Theme.axaml`;
  - `GraphControl` переведен на `nodify:NodifyEditor`;
  - добавлена projection/layout модель `RoadmapNode`, `RoadmapConnection`, `RoadmapGraphProjection`, `RoadmapGraphBuilder`;
  - удалены устаревшие `BlockEdge`, `ContainEdge`, `CompositeItem`;
  - сохранены filters/search highlight, completion checkbox, wanted styling, repeater marker, double tap, drag format, drop handlers and automation ids `RoadmapRoot`/`RoadmapZoomBorder`.
- Tests/build:
  - PASS `dotnet build src\Unlimotion\Unlimotion.csproj --nologo -v:minimal`
  - PASS `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj --nologo -v:minimal`
  - PASS `dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --nologo -v:minimal`
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"` (4/4)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskImportanceUiTests/*"` (2/2)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskListRepeaterMarkerUiTests/*"` (3/3)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj` (247/247)
  - PASS `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --list-tests` (20 tests discovered)
  - PASS `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/Major_tabs_can_be_opened_from_main_window"` (1/1)
  - PASS `rg -n "AvaloniaGraphControl|GraphPanel|BlockEdge|ContainEdge|CompositeItem" src -S` (no matches)
  - PASS `git diff --check` (only LF/CRLF warnings)
  - BLOCKED `dotnet test src\Unlimotion.sln --no-build --nologo -v:minimal`: Microsoft.Testing.Platform rejected unknown `--nologo`
  - BLOCKED `dotnet test src\Unlimotion.sln --no-build`: timed out after 10 minutes without diagnostics; next-best full TUnit project run passed 247/247 and targeted AppAutomation Headless roadmap scenario passed.
- Non-blocking warnings: existing package vulnerability warnings (`AutoMapper`, `Tmds.DBus.Protocol`, `System.Drawing.Common`), preview SDK warning, LF/CRLF warnings, Avalonia telemetry banner.

### Post-EXEC Review
- Статус: PASS
- Проверено:
  - old graph package/API references removed from `src`;
  - Nodify editor preserves roadmap automation ids used by AppAutomation;
  - projection keeps typed `Contains`/`Blocks` connections and removes self-loop workaround for isolated nodes;
  - UI tests cover render, automation id, completion binding, search highlight, double tap details toggle, wanted styling and repeater marker;
  - unrelated pre-existing `AGENTS.md` modification left untouched.
- Residual risk:
  - deterministic layered layout is not byte-for-byte equivalent to old `SugiyamaScheme`; it is covered for graph structure and UI visibility, but visual spacing may still need manual product tuning on very large graphs.

### Follow-up 2026-04-29: left-to-right roadmap layout
- Причина: после визуальной проверки Nodify-граф выглядел как нагромождение, часть стрелок шла не в сторону дерева развития.
- Изменения:
  - contain-связи возвращены к доменному направлению `child -> parent`;
  - block-связи оставлены `blocker -> blocked`, но теперь участвуют в расчете слоя;
  - слой узла считается топологически по направленным связям, чтобы источник связи был левее приемника и стрелки шли слева направо;
  - вертикальная позиция считается по containment tree, чтобы дочерние узлы группировались рядом со своим родителем;
  - ширина roadmap node считается по содержимому с минимальной и максимальной границей, чтобы короткие задачи не занимали фиксированную ширину длинных задач;
  - connection anchors фиксированы как `Tail.RightAnchor -> Head.LeftAnchor`, добавлен `IsLeftToRight`;
  - Nodify `LineConnection` получил horizontal orientation and corner radius.
- Verification:
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=artifacts\codex-test-bin\ -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"` (4/4)
  - PASS `dotnet build src\Unlimotion\Unlimotion.csproj --nologo -v:minimal`
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskImportanceUiTests/*"` (2/2)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=artifacts\codex-repeater-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/TaskListRepeaterMarkerUiTests/*"` (3/3)
  - PASS `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -p:BaseOutputPath=artifacts\codex-headless-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowHeadlessTests/Major_tabs_can_be_opened_from_main_window"` (1/1)
  - PASS `git diff --check` (only LF/CRLF warnings)
  - BLOCKED first direct roadmap test run to default output: `src/Unlimotion.Desktop\bin\Debug\net10.0\Unlimotion.dll` was locked by a running IDE/debug host; rerun used isolated output path.

### Follow-up 2026-04-29: dynamic node sizing and live graph updates
- Причина: после ручной проверки стало видно, что ширина узлов была рассчитана заранее, не следовала за rename, новые задачи не всегда сразу появлялись, удаленные могли оставаться, а порядок узлов недостаточно уменьшал пересечения стрелок.
- Изменения:
  - `RoadmapNode.Width` теперь обновляется по фактическому `Border.Bounds.Width` после layout, а не по аппроксимации длины title;
  - `RoadmapConnection` слушает изменения `RoadmapNode.Width/Location` and raises `Source/Target`, чтобы anchor переезжал вместе с реальным размером узла;
  - `GraphControl` подписывается на изменения текущих `TaskItemViewModel`, relation collections and nested `SubTasks`, поэтому rename/create/delete пересобирают открытую карту;
  - layout получил barycentric layer ordering pass, который поощряет одинаковый порядок источников и приемников и уменьшает пересечения направленных связей;
  - `RoadmapGraphUiTests` расширены до 7 tests: dynamic width on rename, live create/delete, and crossing-order characterization.
- Verification:
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-roadmap-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"` (7/7)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-importance-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/TaskImportanceUiTests/*"` (2/2)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-repeater-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/TaskListRepeaterMarkerUiTests/*"` (3/3)
  - PASS `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-headless-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowHeadlessTests/Major_tabs_can_be_opened_from_main_window"` (1/1)
  - PASS `dotnet build src\Unlimotion\Unlimotion.csproj --nologo -v:minimal -p:BaseOutputPath=<workspace>\artifacts\codex-unlimotion-build\ -p:UseSharedCompilation=false`
  - BLOCKED parallel run of repeater/headless with shared project `obj`: Fody/MSBuild locked `Unlimotion.ViewModel.dll/pdb`; sequential reruns after `dotnet build-server shutdown` passed.

### Follow-up 2026-04-30: targeted content refresh and shorter arrows
- Причина: пользователь заметил риск тормозов из-за полной пересборки графа на каждое изменение и попросил дополнительно минимизировать длину стрелок, особенно вертикальную компоненту block-связей.
- Изменения:
  - `TaskItemViewModel.Title` теперь уведомляет `TitleWithoutEmoji`, `Emoji` and `OnlyTextTitle`, поэтому rename обновляет текст roadmap node без пересборки projection;
  - `GraphControl` разделяет content-only changes and structural changes: title/description/repeater/wanted visual changes обновляют highlight/биндинги, а полная пересборка остается для relation collections, create/delete and filter-affecting properties;
  - structural rebuild теперь применяет projection диффом: существующие `RoadmapNode` и `RoadmapConnection` переиспользуются по stable id/key, stale connections отписываются от node events через `Dispose`;
  - layout после crossing minimization выполняет weighted row adjustment with order-preserving projection: block-связи имеют больший вес, поэтому связанные узлы подтягиваются по Y без увеличения пересечений внутри слоя;
  - `RoadmapGraphUiTests` расширены до 8 tests: добавлена проверка, что длинная block-связь становится преимущественно горизонтальной, и усилена проверка rename без замены node instance.
- Verification:
  - PASS `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj --nologo -v:minimal -p:BaseOutputPath=<workspace>\artifacts\codex-compile-bin\ -p:UseSharedCompilation=false`
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-roadmap-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"` (8/8)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-importance-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/TaskImportanceUiTests/*"` (2/2)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-repeater-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/TaskListRepeaterMarkerUiTests/*"` (3/3)
  - PASS `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-headless-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowHeadlessTests/Major_tabs_can_be_opened_from_main_window"` (1/1)
  - PASS `dotnet build src\Unlimotion\Unlimotion.csproj --nologo -v:minimal -p:BaseOutputPath=<workspace>\artifacts\codex-unlimotion-build\ -p:UseSharedCompilation=false`
  - PASS `git diff --check` (only LF/CRLF warnings)

### Follow-up 2026-04-30: filter rebuild and layer compaction
- Причина: пользователь заметил, что после изменения фильтров открытая карта не пересчитывалась, а на плотном графе оставались длинные пересекающиеся стрелки.
- Изменения:
  - `GraphControl` теперь явно подписывается на `EmojiFilters` и `EmojiExcludeFilters`, включая изменения коллекций and `EmojiFilter.ShowTasks`;
  - изменения фильтров запускают throttled structural rebuild открытого roadmap graph, поэтому скрытые/возвращенные фильтром задачи сразу меняют projection and layout;
  - `RoadmapGraphBuilder` после построения слоев выполняет safe layer span compaction: источник связи подтягивается вправо ближе к приемнику, если это не ломает инвариант `tail layer < head layer` для исходящих направленных связей;
  - compacted layers уменьшают длину contain/block стрелок и снижают количество широких диагоналей, которые пересекают весь экран;
  - `RoadmapGraphUiTests` расширены до 10 tests: добавлены проверка перестройки карты при изменении filter `ShowTasks` and projection-test на сокращение layer span без пересечений.
- Verification:
  - PASS `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj --nologo -v:minimal -p:BaseOutputPath=<workspace>\artifacts\codex-compile-bin\ -p:UseSharedCompilation=false`
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-roadmap-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"` (10/10)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-importance-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/TaskImportanceUiTests/*"` (2/2)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-repeater-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/TaskListRepeaterMarkerUiTests/*"` (3/3)
  - PASS `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-headless-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowHeadlessTests/Major_tabs_can_be_opened_from_main_window"` (1/1)
  - PASS `dotnet build src\Unlimotion\Unlimotion.csproj --nologo -v:minimal -p:BaseOutputPath=<workspace>\artifacts\codex-unlimotion-build\ -p:UseSharedCompilation=false`
  - PASS `git diff --check` (only LF/CRLF warnings)

### Follow-up 2026-04-30: minimap and viewport controls
- Причина: пользователь попросил добавить mini-preview and explicit controls for graph zoom/viewport position.
- Изменения:
  - `GraphControl` получил overlay поверх `NodifyEditor` с `nodify:Minimap`, привязанной к `RoadmapNodes`, `ViewportLocation` and `ViewportSize` текущего editor;
  - добавлена компактная toolbar с automation ids для zoom in/out, fit, reset and pan left/right/up/down;
  - кнопки zoom используют `NodifyEditor.ZoomIn/ZoomOut`, fit/restart сохраняют прежнюю клавиатурную семантику, pan меняет `ViewportLocation`;
  - `Minimap.Zoom` обработан через routed event args and forwards zoom to `RoadmapEditor.ZoomAtPosition` when event exposes zoom/location;
  - `RoadmapGraphUiTests` расширены до 11 tests: добавлена проверка, что minimap/control overlay materializes in headless and changes `ViewportZoom`/`ViewportLocation`.
- Verification:
  - PASS `dotnet build src\Unlimotion\Unlimotion.csproj --nologo -v:minimal -p:BaseOutputPath=<workspace>\artifacts\codex-minimap-build\ -p:UseSharedCompilation=false`
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-roadmap-minimap-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"` (11/11)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-importance-minimap-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/TaskImportanceUiTests/*"` (2/2)
  - PASS `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-repeater-minimap-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/TaskListRepeaterMarkerUiTests/*"` (3/3)
  - PASS `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -p:BaseOutputPath=<workspace>\artifacts\codex-headless-minimap-bin\ -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowHeadlessTests/Major_tabs_can_be_opened_from_main_window"` (1/1)

## Approval
Подтверждено пользователем: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Собрать instruction stack и локальные правила | 0.95 | Нет | Подготовить spec | Нет | Нет | Локальный AGENTS требует QUEST и UI tests | `AGENTS.md`, `AGENTS.override.md`, central instructions |
| SPEC | Исследовать текущий graph contract | 0.9 | Нет | Зафиксировать parity scope | Нет | Нет | Graph behavior включает layout, filters, search, markers, drag/drop and zoom keys | `GraphControl.axaml`, `GraphControl.axaml.cs`, tests |
| SPEC | Проверить Nodify package/API feasibility | 0.82 | Точная compile совместимость проверяется на EXEC | Создать spec | Нет | Нет | `NodifyAvalonia 6.6.0` latest on NuGet and docs require `avares://Nodify/Theme.axaml` | NuGet, Nodify examples |
| SPEC | Создать и self-review spec | 0.9 | Нет | Запросить подтверждение пользователя | Да | Да, ожидается фраза `Спеку подтверждаю` | Central QUEST запрещает code changes до подтверждения | `specs/2026-04-28-nodify-graph-migration.md` |
| EXEC | Заменить graph dependency and view | 0.9 | Нет | Добавить parity tests | Нет | Да, пользователь подтвердил EXEC | NodifyEditor внедрен с сохранением automation ids and graph interactions | `Directory.Packages.props`, `Unlimotion.csproj`, `App.axaml`, `GraphControl.axaml`, `GraphControl.axaml.cs`, `Views/Graph/*` |
| EXEC | Добавить UI/projection coverage | 0.9 | Нет | Запустить targeted checks | Нет | Нет | Migration touches UI behavior, so coverage added per AGENTS.override.md | `RoadmapGraphUiTests.cs`, `TaskListRepeaterMarkerUiTests.cs` |
| EXEC | Выполнить verification and post-review | 0.88 | Полный `dotnet test src\Unlimotion.sln --no-build` завис без диагностик | Завершить задачу | Нет | Нет | Targeted roadmap/AppAutomation checks and full main TUnit project passed; solution-wide blocker documented | build/test commands, `git diff --check`, `rg` |
| EXEC | Исправить left-to-right layout после визуальной проверки | 0.9 | Нет | Завершить задачу | Нет | Да, пользователь сообщил визуальный дефект | Direction and layering now keep directed edges left-to-right: child/blocker on the left, parent/blocked on the right | `RoadmapGraphBuilder.cs`, `RoadmapConnection.cs`, `GraphControl.axaml`, `RoadmapGraphUiTests.cs` |
| EXEC | Исправить dynamic sizing/live refresh/crossing order | 0.9 | Нет | Завершить задачу | Нет | Да, пользователь сообщил follow-up дефекты | Node width now comes from rendered layout, graph listens to title/relation/nested collection changes, and layer ordering uses barycentric sweeps to reduce crossings | `RoadmapNode.cs`, `RoadmapConnection.cs`, `RoadmapGraphBuilder.cs`, `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Снизить частоту пересборок and shorten arrows | 0.88 | Нет | Завершить задачу | Нет | Да, пользователь сообщил performance/layout follow-up | Content-only task changes now update bindings/highlights without projection rebuild; structural rebuilds preserve node/connection instances; layout pulls connected rows together with stronger weight for block edges | `TaskItemViewModel.cs`, `GraphControl.axaml.cs`, `RoadmapConnection.cs`, `RoadmapGraphBuilder.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Исправить filter-triggered rebuild and compact long edges | 0.88 | Нет | Завершить задачу | Нет | Да, пользователь сообщил follow-up дефекты | Graph now listens to filter ShowTasks changes directly; layer compaction shortens directed edges by moving tails right when safe | `GraphControl.axaml.cs`, `RoadmapGraphBuilder.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Добавить minimap and viewport controls | 0.9 | Нет | Завершить задачу | Нет | Да, пользователь подтвердил добавление | Nodify Minimap is bound to the editor viewport; overlay buttons expose zoom, fit, reset and pan controls with UI coverage | `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
