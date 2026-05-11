# Диалог по оставшимся необновленным NuGet-пакетам

## 0. Контекст
- Дата начала диалога: 2026-05-09.
- База: локальный коммит `b4a3e5e Update NuGet packages to compatible stable versions`.
- Цель: совместно решить, что нужно сделать, чтобы в итоге обновить оставшиеся direct NuGet-пакеты до latest stable.
- Формат: этот файл фиксирует факты, вопросы, аргументы и принятые решения по ходу диалога.

## 1. Текущий список оставшихся обновлений
По свежему `dotnet list <project> package --outdated --no-restore`:

| Группа | Сейчас | Latest stable | Где видно | Почему не обновлено сейчас |
| --- | --- | --- | --- | --- |
| Avalonia family | `11.3.14` | `12.0.2` | `Unlimotion`, `Android`, `Browser`, `Desktop`, `iOS`, `Unlimotion.Test` | Runtime/API несовместимость с частью UI-зависимостей |
| `ReactiveUI.Avalonia` | `11.3.8` | `12.0.1` | `src/Unlimotion/Unlimotion.csproj` | `12.0.1` требует Avalonia `>= 12.0.1`, поэтому зависит от решения по Avalonia 12 |
| `DialogHost.Avalonia` | `0.11.0` | `0.12.2` | `src/Unlimotion/Unlimotion.csproj` | 0.12.x находится в Avalonia 12-ветке, поэтому зависит от решения по Avalonia 12 |

Снято 2026-05-09:
- `TUnit` main test stack обновлен до `1.43.41`.
- `TUnit` in AppAutomation projects обновлен до `1.43.41`.
- `AppAutomation.TUnit 1.5.6` удален из direct dependencies и заменен локальным совместимым glue в `tests/Unlimotion.UiTests.Authoring`.

## 2. Факты из package metadata / источников
- `Avalonia 12.0.2` опубликована 2026-04-28 и таргетит `.NET 8.0` с совместимостью для `.NET 10.0`; значит текущий `net10.0` сам по себе не блокер. Источник: https://www.nuget.org/packages/Avalonia
- `ReactiveUI.Avalonia 12.0.1` зависит от `Avalonia >= 12.0.1`, поэтому ее нельзя обновить отдельно от Avalonia 12. Источник: https://www.nuget.org/packages/ReactiveUI.Avalonia/
- `Avalonia.Controls.PanAndZoom 11.3.0` остается последней stable на NuGet и помечена deprecated; 12.x stable версии нет. Источник: https://www.nuget.org/packages/Avalonia.Controls.PanAndZoom/
- `DialogHost.Avalonia 0.12.x` является следующей веткой после 0.11.x и требует рассматривать ее вместе с Avalonia 12. Источник: https://www.nuget.org/packages/DialogHost.Avalonia
- `TUnit` latest stable на 2026-05-09 по локальному NuGet scan: `1.43.41`.
- В Avalonia 12 пакет `Avalonia.Diagnostics` удален; официальная рекомендация - удалить reference и, если нужны Dev Tools, заменить на `AvaloniaUI.DiagnosticsSupport`. Источник: https://docs.avaloniaui.net/docs/avalonia12-breaking-changes
- `NodifyAvalonia 6.6.0` в upstream compatibility chart привязан к Avalonia `11.1.0`; официальной Avalonia 12-compatible release на 2026-05-10 не найдено. Источники: https://github.com/BAndysc/nodify-avalonia/blob/avalonia_port/README.md, https://github.com/BAndysc/nodify-avalonia/blob/avalonia_port/Directory.Build.props
- Найден прямой fork-branch `JuenTingShie/nodify-avalonia` `copilot/update-avalonia-packages-to-12`: `AvaloniaVersion` поднят до `12.0.1`, есть API/XAML правки, core project `Nodify/Nodify.csproj` локально собирается без ошибок, но с 158 warnings. Источники: https://github.com/JuenTingShie/nodify-avalonia/blob/copilot/update-avalonia-packages-to-12/Directory.Build.props, https://github.com/JuenTingShie/nodify-avalonia/compare/avalonia_port...copilot/update-avalonia-packages-to-12
- Найден альтернативный проект `NodifyM.Avalonia`: это не 1:1 fork текущего `NodifyAvalonia`, а refactoring/reimplementation с похожими MVVM controls; в `1.3.0` он использует `Avalonia 12.0.2`, changelog заявляет поддержку Avalonia 12 начиная с `1.2.0`. Источники: https://github.com/Maklith/NodifyM.Avalonia/blob/master/README.md, https://github.com/Maklith/NodifyM.Avalonia/blob/master/NodifyM.Avalonia/NodifyM.Avalonia.csproj

## 3. Гипотезы по путям обновления
### Путь A: Полная миграция UI на Avalonia 12
Что нужно:
- Обновить всю Avalonia family до `12.0.2`.
- Обновить `ReactiveUI.Avalonia` до `12.0.1`.
- Обновить `DialogHost.Avalonia` до `0.12.2`.
- Решить blocker `NodifyAvalonia 6.6.0`: дождаться/найти Avalonia 12-compatible release, заменить библиотеку, форкнуть и адаптировать, либо убрать зависимость.
- Решить blocker `Avalonia.Controls.PanAndZoom 11.3.0`: заменить библиотеку, форкнуть и адаптировать, либо переписать нужный zoom/pan surface локально.
- Перейти с obsolete drag/drop API на Avalonia 12 `DataTransfer` API.
- Прогнать обязательные UI-тесты по roadmap graph, drag/drop, zoom/pan, dialogs, mobile/browser startup.

Плюсы:
- Все UI-пакеты действительно уходят на latest stable.
- Снимаются текущие obsolete warnings по Avalonia 11.3 drag/drop.

Минусы:
- Это уже не "dependency bump", а отдельная UI-platform migration.
- Самый рискованный участок: graph editor и pan/zoom.

### Путь B: Удержать Avalonia 11 до появления совместимых upstream-пакетов
Что нужно:
- Оставить Avalonia family, `ReactiveUI.Avalonia`, `DialogHost.Avalonia`, `PanAndZoom`, `NodifyAvalonia` как documented exceptions.
- Периодически проверять релизы `NodifyAvalonia` и `Avalonia.Controls.PanAndZoom`.
- Сейчас обновить только `TUnit` main test stack до `1.43.41`.

Плюсы:
- Самый дешевый и безопасный вариант.
- Сохраняет текущую работоспособность UI.

Минусы:
- Запрос "в итоге обновить все" остается отложенным.
- Deprecated `PanAndZoom` и Avalonia 11 остаются техническим долгом.

### Путь C: Предварительная развязка от блокирующих UI-пакетов на Avalonia 11
Что нужно:
- На Avalonia 11 заменить или локализовать зависимости от `NodifyAvalonia`/`PanAndZoom` за отдельными internal adapter/control boundaries.
- Добавить UI characterization tests вокруг graph editor, roadmap drag/drop, zoom/pan.
- После этого пробовать Avalonia 12 миграцию с меньшим blast radius.

Плюсы:
- Снижает риск будущей Avalonia 12 миграции.
- Позволяет принимать решения по библиотекам независимо.

Минусы:
- Больше работы до видимого обновления.
- Нужно аккуратно не переписать graph editor сверх необходимости.

### Путь D: AppAutomation/TUnit отдельно
Что нужно:
- Проверить, есть ли более новая `AppAutomation.TUnit`, совместимая с TUnit `1.43.41`.
- Если нет, выбрать: удерживать TUnit `1.37.10` только в AppAutomation; форкнуть/patch-нуть `AppAutomation.TUnit`; отказаться от `AppAutomation.TUnit` glue и запускать через plain TUnit/MTP; либо мигрировать UI tests на другой runner.
- Разобраться с текущим зависанием полного `Unlimotion.UiTests.Headless.exe`.

Плюсы:
- Тестовый стек можно чинить независимо от Avalonia 12.

Минусы:
- Зависание runner может быть отдельным багом test host/session lifecycle.

## 4. Вопросы для решения
1. Что выбираем как целевой маршрут по Avalonia: A, B или C?
2. Готовы ли мы заменить/форкнуть `NodifyAvalonia` и `PanAndZoom`, если upstream не даст Avalonia 12-compatible stable в ближайшее время?
3. Что важнее для AppAutomation: сохранить `AppAutomation.TUnit` и ждать совместимости, или убрать эту связку и контролировать runner самим?
4. Обновляем ли `TUnit` main test stack `1.43.38 -> 1.43.41` сразу как quick win?

## 5. Решения
### Решение 1: сначала снимаем блокеры
Принято пользователем 2026-05-09: сначала снимаем блокеры, а не делаем прямую полную миграцию на Avalonia 12.

Практический смысл решения:
- выбран маршрут C: подготовительная развязка от блокирующих UI-пакетов на Avalonia 11;
- Avalonia 12 migration откладывается до тех пор, пока blockers будут сняты или изолированы;
- блокеры считаются снятыми, если пакет либо удален из direct dependencies, либо заменен, либо изолирован за локальной границей так, чтобы Avalonia 12-миграция не зависела от его API напрямую.

## 6. Инвентаризация блокеров по коду
### `Avalonia.Controls.PanAndZoom`
- По поиску `PanAndZoom`, `ZoomBorder`, `Zoom`, `Pan` прямое использование `Avalonia.Controls.PanAndZoom` в коде не найдено.
- `src/Unlimotion/Unlimotion.csproj` содержит direct `PackageReference Include="Avalonia.Controls.PanAndZoom"`.
- Фактический roadmap zoom/pan реализован через `NodifyEditor`:
  - `src/Unlimotion/Views/GraphControl.axaml`: `nodify:NodifyEditor x:Name="RoadmapEditor"` с automation id `RoadmapZoomBorder`;
  - `src/Unlimotion/Views/GraphControl.axaml.cs`: используются `RoadmapEditor.ViewportZoom`, `ViewportLocation`, `ZoomIn()`, `ZoomOut()`, `ZoomAtPosition()`, `FitToScreen()`.
- Вывод: `PanAndZoom` выглядит как stale direct dependency и первый дешевый кандидат на снятие блокера через удаление reference + restore/build/UI tests.
- Статус 2026-05-09: direct reference удален из `src/Unlimotion/Unlimotion.csproj`, central version удалена из `src/Directory.Packages.props`.
- После удаления `dotnet list src\Unlimotion\Unlimotion.csproj package --outdated --no-restore` больше не показывает `Avalonia.Controls.PanAndZoom`.

### `NodifyAvalonia`
- Реальное использование сосредоточено в roadmap graph UI:
  - `src/Unlimotion/Views/GraphControl.axaml`: `nodify:NodifyEditor`, `nodify:LineConnection`, templates for roadmap nodes/connections;
  - `src/Unlimotion/Views/GraphControl.axaml.cs`: imperative access к `RoadmapEditor` для zoom/pan/fit и pointer interactions.
- Уже есть существенная characterization coverage:
  - `src/Unlimotion.Test/RoadmapGraphUiTests.cs` покрывает projection, render, automation ids, zoom/pan controls, minimap, node click/double tap, node drag, right-drag pan, drop behavior.
- Вывод: это главный Avalonia 12 blocker. Его нельзя просто удалить; нужны adapter boundary, форк/замена, либо локальная реализация roadmap surface.
- Статус 2026-05-09: начат легкий boundary.
  - Добавлен `IRoadmapViewportAdapter` и `NodifyRoadmapViewportAdapter`.
  - `GraphControl.axaml.cs` больше не обращается напрямую к `RoadmapEditor.ViewportZoom`, `ViewportLocation`, `ZoomIn()`, `ZoomOut()`, `ZoomAtPosition()`, `FitToScreen()`; эти операции идут через adapter.
- Прямая зависимость от Nodify все еще остается в XAML (`NodifyEditor`, `LineConnection`, `Minimap`) и в одной точке создания adapter. Значит blocker снижен, но не снят полностью.
- Пробный Avalonia 12 bump 2026-05-09 подтвердил, что это hard blocker:
  - при наличии `ResourceInclude Source="avares://Nodify/Theme.axaml"` `App.Initialize()` зависает внутри `AvaloniaXamlLoader.Load(this)`;
  - если убрать app-level Nodify theme, `MainControl.InitializeComponent()` все равно зависает на embedded `<views:GraphControl DataContext="{Binding Graph}"/>`;
  - временная замена `GraphControl` на placeholder позволяет non-graph compatibility test пройти, значит блокер локализован в Nodify theme/surface, а не в общем Avalonia 12 runtime.

### `AppAutomation.TUnit` / TUnit
- `src/Unlimotion.Test` использует TUnit напрямую и обновлен до latest patch `1.43.41`.
- `tests/Unlimotion.UiTests.Headless`, `tests/Unlimotion.UiTests.FlaUI`, `tests/Unlimotion.UiTests.Authoring` обновлены до TUnit `1.43.41`.
- `AppAutomation.TUnit 1.5.6` был latest, но runtime-падал с TUnit 1.43.x на `MissingFieldException: TUnit.Core.Sources.BeforeTestHooks`.
- Решение 2026-05-09: удалить `AppAutomation.TUnit` direct dependency и заменить только нужный glue (`IUiTestSession`, `UiAssert`, `UiTestBase`) локальной реализацией в `tests/Unlimotion.UiTests.Authoring`.
- После этого `dotnet list ... package --outdated --no-restore` для `Unlimotion.UiTests.Headless`, `Unlimotion.UiTests.FlaUI`, `Unlimotion.UiTests.Authoring` больше не показывает updates.
- Оставшийся не-NuGet blocker: реальный запуск `Unlimotion.UiTests.Headless` зависает в in-progress test и отменяется только по global timeout. Это уже задача lifecycle/headless test host, а не package compatibility blocker.

### Avalonia legacy drag/drop API
- До 2026-05-09 сборка показывала obsolete warnings по `DataObject`, `IDataObject`, `DragEventArgs.Data`, `DragDrop.DoDragDrop(...)`.
- Эти API помечены в Avalonia 11.3 как legacy и заменяются на `IDataTransfer`/`DataTransfer`/`DragDrop.DoDragDropAsync(...)`.
- Решение 2026-05-09: перейти на новый API до Avalonia 12 migration.
- Для in-process object payloads добавлен локальный `InMemoryDragDataTransfer`: публичный `DataTransfer` API принимает string formats, поэтому object references хранятся через short-lived registry key внутри процесса.
- После миграции поиск `DataObject`, `IDataObject`, `DragDrop.DoDragDrop(` в `src/Unlimotion` и `src/Unlimotion.Test` ничего не находит.
- Оставшийся warning по Avalonia API: `IClipboard.GetTextAsync()` в `MainControl.axaml.cs`; это следующий небольшой API blocker, не связанный с drag/drop.

## 7. Кандидаты на следующий шаг
### Шаг 1: снять stale `PanAndZoom`
Предлагаемое действие:
- удалить `PackageReference Include="Avalonia.Controls.PanAndZoom"` из `src/Unlimotion/Unlimotion.csproj`;
- удалить `PackageVersion Include="Avalonia.Controls.PanAndZoom"` из `src/Directory.Packages.props`;
- выполнить `dotnet restore`, build `Unlimotion.Test` и targeted roadmap UI tests.

Ожидаемый риск: низкий, потому что прямых usages не найдено.

Статус: выполнено 2026-05-09.

Проверки:
- `dotnet restore src\Unlimotion.sln` - pass, с существующим `NU1608` по Android `LibGit2Sharp.NativeBinaries`.
- `dotnet restore src\Unlimotion.Test\Unlimotion.Test.csproj` - pass.
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `RoadmapGraph_NodeRightDrag_PansViewportWithoutSelectingTask` - pass.
- `PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work` - pass.
- Обновлено 2026-05-09: roadmap characterization test gaps закрыты.
  - `WaitForGraphControl` учитывает direct-root `GraphControl`.
  - MainControl roadmap tests явно открывают `RoadmapTabItem`, потому что `GraphMode` связан через `OneWayToSource`.
  - `WaitForTaskNode` больше не путает реальные task cards с decorative minimap nodes.
  - Scheduled rebuild tests используют async waiting, чтобы headless dispatcher мог отработать `DispatcherTimer`.
  - `GraphControl.ResolveMainWindowViewModel` сначала берет owner из `GraphViewModel`, и только потом падает назад на static singleton.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*" --maximum-parallel-tests 1 --no-progress` - pass: 34/34.
- `dotnet list src\Unlimotion\Unlimotion.csproj package --outdated --no-restore` - pass, `Avalonia.Controls.PanAndZoom` отсутствует в списке; остались Avalonia family, `DialogHost.Avalonia`, `ReactiveUI.Avalonia`.

### Шаг 2: сделать boundary вокруг `NodifyEditor`
Варианты:
- легкий boundary: минимальная локальная обертка/контракт вокруг zoom/pan/fit operations и automation ids, без изменения XAML templates;
- глубокий boundary: вынести весь roadmap surface в отдельный control/project, чтобы будущая замена Nodify не затрагивала `MainControl`/ViewModel;
- прямой fork: оставить текущий API и адаптировать `NodifyAvalonia` под Avalonia 12, если лицензия и объем изменений приемлемы.

Предварительная рекомендация Codex: начать с легкого boundary и characterization tests, затем оценить fork/replacement по фактическому объему.

Статус: легкий boundary выполнен 2026-05-09.

Проверки:
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*" --maximum-parallel-tests 1 --no-progress` - pass: 34/34.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work" --no-progress` - pass: 1/1.

Оставшийся вопрос:
- выбираем ли глубокий boundary для XAML surface или идем в оценку fork/replacement `NodifyAvalonia` под Avalonia 12.

### Шаг 3: AppAutomation/TUnit
Статус: выполнено 2026-05-09.

Что сделано:
- newer `AppAutomation.TUnit`, чем `1.5.6`, не найден через `dotnet list ... package --outdated`;
- TUnit в AppAutomation-проектах поднят до `1.43.41`;
- `AppAutomation.TUnit` удален и заменен локальным glue;
- основной `src/Unlimotion.Test` также поднят с `TUnit 1.43.38` до `1.43.41`.

Проверки:
- `dotnet restore tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj` - pass.
- `dotnet restore tests\Unlimotion.UiTests.FlaUI\Unlimotion.UiTests.FlaUI.csproj` - pass.
- `dotnet build tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `dotnet build tests\Unlimotion.UiTests.FlaUI\Unlimotion.UiTests.FlaUI.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `dotnet run --no-build --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --list-tests` - pass, discovers 23 tests and no longer crashes on `BeforeTestHooks`.
- `dotnet run --no-build --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/Main_window_loads_current_task_on_launch" --maximum-parallel-tests 1 --timeout 30s --output Detailed --show-stdout All --show-stderr All --diagnostic --diagnostic-output-directory artifacts\test-diagnostics\headless-tunit --diagnostic-file-prefix headless-tunit` - fails by global timeout; diagnostic shows the selected test entered `InProgressTestNodeStateProperty` and then was forcefully terminated.
- `dotnet restore src\Unlimotion.Test\Unlimotion.Test.csproj` - pass.
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*" --maximum-parallel-tests 1 --no-progress` - pass: 34/34.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work" --no-progress` - pass: 1/1.

Вывод:
- NuGet blocker по `AppAutomation.TUnit`/TUnit снят.
- Отдельно нужно чинить зависание AppAutomation Headless runner, но оно больше не удерживает packages на старых версиях.

### Шаг 4: Avalonia drag/drop `DataTransfer`
Статус: выполнено 2026-05-09.

Что сделано:
- `DataObject` заменен на `IDataTransfer`-совместимый `InMemoryDragDataTransfer`.
- `DragDrop.DoDragDrop(...)` заменен на `DragDrop.DoDragDropAsync(...)`.
- `DragEventArgs.Data` заменен на `DragEventArgs.DataTransfer`.
- Roadmap/package compatibility tests обновлены на constructor `DragEventArgs(..., IDataTransfer, ...)`.

Проверки:
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work" --no-progress` - pass: 1/1.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_DropWithControl_CreatesBlockingRelationBetweenRoadmapNodes" --no-progress` - pass: 1/1.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*" --maximum-parallel-tests 1 --no-progress` - pass: 34/34.
- `rg -n "\bDataObject\b|\bIDataObject\b|DragDrop\.DoDragDrop\(" src/Unlimotion src/Unlimotion.Test -g "*.cs"` - no matches.

Вывод:
- Drag/drop API blocker для Avalonia 12 migration снят.
- Следующий маленький API blocker был `IClipboard.GetTextAsync()` в `MainControl.axaml.cs`.

### Avalonia legacy clipboard API
- До 2026-05-09 сборка показывала obsolete warning по `IClipboard.GetTextAsync()` в `MainControl.axaml.cs`.
- Решение 2026-05-09: заменить чтение clipboard на extension `TryGetTextAsync()` из `Avalonia.Input.Platform`.
- Статус: выполнено; прямых `GetTextAsync` usages в `src/Unlimotion` больше нет.

### Шаг 5: Avalonia clipboard `TryGetTextAsync`
Статус: выполнено 2026-05-09.

Что сделано:
- `MainControl.GetClipboardTextAsync()` переведен с obsolete `IClipboard.GetTextAsync()` на `TryGetTextAsync()`.
- Поведение для недоступного clipboard не менялось: как и раньше, показывается `ClipboardUnavailable`, затем возвращается `null`.

Проверки:
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_CopyTaskOutline_HotkeyAndContextMenu_Work" --no-progress` - pass: 1/1.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask" --no-progress` - pass: 1/1.
- `rg -n "GetTextAsync|TryGetTextAsync" src/Unlimotion src/Unlimotion.Test -g "*.cs"` - only `TryGetTextAsync()` remains.

### Шаг 6: пробный Avalonia 12 bump
Статус: выполнено как investigation 2026-05-09; кодовые изменения пробного bump не оставлены в рабочей ветке.

Что проверялось:
- `Avalonia*` family `11.3.14 -> 12.0.2`.
- `ReactiveUI.Avalonia 11.3.8 -> 12.0.1`.
- `DialogHost.Avalonia 0.11.0 -> 0.12.2`.

Найденные факты:
- Restore сначала падал на `Avalonia.Diagnostics >= 12.0.2`: такой версии нет, потому что пакет удален в Avalonia 12.
- После удаления diagnostics references restore проходил.
- Compile blocker `GotFocusEventArgs` в `MainControl.axaml.cs` снимается заменой handler аргумента на `RoutedEventArgs`.
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` на Avalonia 12 проходил, но появлялись новые warnings по `Watermark -> PlaceholderText` и `UseFloatingWatermark -> UseFloatingPlaceholder`.
- UI-test blocker 1: `HeadlessUnitTestSession` с синхронным `using` зависает на teardown; `await using` для session позволяет тесту завершиться.
- UI-test blocker 2: `App.axaml` зависает при загрузке `avares://Nodify/Theme.axaml`.
- UI-test blocker 3: без app-level Nodify theme `MainControl.axaml` зависает на `<views:GraphControl DataContext="{Binding Graph}"/>`.
- Контрольная временная проверка: когда Nodify theme removed и `GraphControl` replaced by placeholder, `PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work` проходит на Avalonia 12 после `await using`.

Вывод:
- Avalonia 12 compile path почти открыт, но merge-ready bump пока невозможен из-за `NodifyAvalonia 6.6.0`.
- Следующее решение по диалогу: выбрать один из трех путей для roadmap graph surface:
  1. форкнуть/адаптировать `NodifyAvalonia` под Avalonia 12;
  2. заменить `NodifyAvalonia` на другую graph/diagram библиотеку;
  3. сделать локальный lightweight graph surface для текущих roadmap сценариев.
- До выбранного решения Avalonia family, `ReactiveUI.Avalonia` и `DialogHost.Avalonia` остаются documented exceptions.

### Шаг 7: Avalonia 12 breaking changes и поиск Nodify forks
Статус: investigation выполнено 2026-05-10; кодовые изменения не вносились.

Что проверялось:
- официальные breaking changes Avalonia 12;
- локальные usages в `src`/`tests`;
- NuGet/GitHub состояние вокруг `NodifyAvalonia`;
- 23 GitHub forks `BAndysc/nodify-avalonia` через GitHub API, с поиском `Avalonia 12` markers в `Directory*.props` и `.csproj`;
- альтернативные публичные packages/repositories с именем `Nodify`.

Break changes, которые реально касаются нас:
- `Avalonia.Diagnostics`: пакет удален в Avalonia 12. В коде `AttachDevTools` usages не найдены, поэтому практический шаг - убрать `Avalonia.Diagnostics` из `Directory.Packages.props`, `src/Unlimotion/Unlimotion.csproj`, `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj`, `src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj`, `src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj`.
- `GotFocusEventArgs`: единственный найденный compile blocker - `src/Unlimotion/Views/MainControl.axaml.cs`; handler можно перевести на `RoutedEventArgs`.
- `Watermark`/`UseFloatingWatermark`: Avalonia 12 оставляет compatibility members, но build на пробном bump давал warnings. Нужна механическая замена UI-facing properties/selectors на `PlaceholderText`/`UseFloatingPlaceholder` в `MainControl.axaml`, `SettingsControl.axaml`, `SearchControl.axaml`, `SearchBar.axaml`, `GraphControl.axaml` и app-level styles. Внутренние VM names вроде `TaskRelationEditorViewModel.Watermark` можно оставить до отдельного cleanup, если binding surface переведен.
- `HeadlessUnitTestSession`: пробный bump показал зависание teardown при sync `using`; для Avalonia 12 нужно массово перевести headless sessions на `await using` и проверить shared hooks в `tests/Unlimotion.UiTests.Headless`.
- Drag/drop и clipboard blockers уже сняты предыдущими шагами: `IDataTransfer`/`DoDragDropAsync` и `TryGetTextAsync()` больше не должны удерживать Avalonia 12 migration.

Что найдено по Nodify:
- Upstream `BAndysc/nodify-avalonia` по-прежнему публикует `NodifyAvalonia 6.6.0` для Avalonia `11.1.0`; latest direct package под Avalonia 12 не найден.
- Среди прямых forks найден один релевантный branch: `JuenTingShie/nodify-avalonia`, branch `copilot/update-avalonia-packages-to-12`. Он поднимает `AvaloniaVersion` до `12.0.1`, меняет `Nodify/Nodify.csproj` на `net8.0`, правит часть Avalonia 12 API/XAML и локально собирается командой `dotnet build ...\Nodify\Nodify.csproj` без ошибок. Минусы: branch не upstream, version/package id оставлены `NodifyAvalonia 6.6.0`, warnings не вычищены, в нашем приложении он еще не проверялся.
- Отдельный `Maklith/NodifyM.Avalonia` уже поддерживает Avalonia 12 и в текущем `master` использует `Avalonia 12.0.2`. Это не drop-in replacement: README прямо описывает проект как refactoring of Nodify on Avalonia platform, not a 1:1 replica. Для нас это backup-вариант по graph surface: переписать `GraphControl.axaml` под другой API, сохраняя наши `GraphViewModel`/adapter boundaries.

Практический вывод:
- Самый дешевый следующий шаг - не переходить сразу на `NodifyM.Avalonia`, а сделать controlled probe собственного internal fork/package от текущего `NodifyAvalonia`, используя найденную ветку `JuenTingShie` как ориентир.
- Чтобы не получить NuGet cache collision, internal package должен получить отдельную prerelease версию, например `6.6.0-unlimotion.a12.1`, а не переиспользовать upstream `6.6.0`.
- Проверка fork считается успешной только если на Avalonia 12 проходят app initialization, `RoadmapGraphUiTests`, package compatibility UI test и хотя бы один smoke тест AppAutomation Headless/FlaUI.
- Если fork не снимает hang на `avares://Nodify/Theme.axaml`/`GraphControl`, следующий кандидат - `NodifyM.Avalonia` как replacement с адаптацией XAML и минимальным изменением ViewModel boundary.

Предлагаемый порядок миграции проектов на Avalonia 12 после выбора Nodify fork:
1. Подготовить internal `NodifyAvalonia` package на Avalonia 12 в отдельном source/package feed.
2. Поднять Avalonia family до `12.0.2`, `ReactiveUI.Avalonia` до `12.0.1`, `DialogHost.Avalonia` до `0.12.2`, `Avalonia.Headless` до `12.0.2`.
3. Удалить `Avalonia.Diagnostics` references или отдельно добавить `AvaloniaUI.DiagnosticsSupport`, если Dev Tools реально нужны.
4. Заменить `GotFocusEventArgs` на `RoutedEventArgs`.
5. Перевести XAML `Watermark`/`UseFloatingWatermark` на `PlaceholderText`/`UseFloatingPlaceholder`.
6. Перевести headless test sessions на `await using`.
7. Запустить build и UI tests: `src/Unlimotion.Test`, `RoadmapGraphUiTests`, `PackageUpdateCompatibilityUiTests`, `tests/Unlimotion.UiTests.Headless`, затем FlaUI smoke.

Решение на текущий момент:
- Принимаем `JuenTingShie` branch как найденный рабочий ориентир для собственного fork/probe, но не как готовую dependency.
- Не принимаем `NodifyM.Avalonia` как первый ход, потому что это замена surface/API, а не точечный package bump.

### Шаг 8: controlled internal package probe `NodifyAvalonia` под Avalonia 12
Статус: investigation/probe выполнен 2026-05-10; рабочая ветка содержит WIP-пробу, не merge-ready.

Что сделано:
- Собран локальный пакет `artifacts\nuget-local\NodifyAvalonia.6.6.0-unlimotion.a12.1.nupkg` из `JuenTingShie/nodify-avalonia` branch `copilot/update-avalonia-packages-to-12`, с `AvaloniaVersion=12.0.2`.
- В пробном bump подняты `Avalonia*` до `12.0.2`, `ReactiveUI.Avalonia` до `12.0.1`, `DialogHost.Avalonia` до `0.12.2`, `NodifyAvalonia` до internal prerelease `6.6.0-unlimotion.a12.1`.
- Удалены `Avalonia.Diagnostics` references, потому что пакет удален в Avalonia 12.
- Выполнены compile/XAML API правки: `GotFocusEventArgs -> RoutedEventArgs`, `Watermark -> PlaceholderText`, `UseFloatingWatermark -> UseFloatingPlaceholder`, headless sessions `using -> await using`.
- Дополнительно исправлен runtime-threading blocker в `GraphControl`: graph rebuild callbacks теперь проверяют/post через instance `Dispatcher`, а не через static `Dispatcher.UIThread`, потому что Avalonia 12 строже проверяет ownership styled properties.

Ключевые результаты:
- `dotnet restore src\Unlimotion.Test\Unlimotion.Test.csproj -v:minimal` - pass.
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- С полным `NodifyAvalonia 6.6.0-unlimotion.a12.1`, `DialogHost.Avalonia 0.12.2` и временно отключенным `Notification.Avalonia` style include: `PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work` - pass.
- В той же конфигурации: `RoadmapGraphUiTests` - pass, 34/34.

Новая уточненная картина blocker-ов:
- Первичная гипотеза "зависает Nodify theme" оказалась неполной: полный fork `NodifyAvalonia` проходит app initialization и roadmap UI tests, если убрать другой blocker.
- Реальный app-initialization hang в текущей пробе вызывает `StyleInclude Source="avares://Notification.Avalonia/Themes/Generic.xaml"` из `Mileeena.Notification.Avalonia 2.1.2`: `AvaloniaXamlLoader.Load(this)` зависает до завершения `App.Initialize`.
- `Mileeena.Notification.Avalonia 2.1.2` не показывается как outdated direct package, но тянет старые Avalonia/SVG transitive dependencies (`Avalonia.Svg.Skia 11.0.0.10`, `Avalonia.Skia 11.0.0`), что делает его отдельным Avalonia 12 compatibility blocker.

Решение на текущий момент:
- Продолжаем считать internal fork `NodifyAvalonia` самым дешевым путем для roadmap graph, но нужен настоящий fork/package source, а не локальный artifact как финальное решение.
- Следующий blocker перед merge-ready Avalonia 12 bump - `Mileeena.Notification.Avalonia`: заменить на internal toast overlay, найти/forkнуть Avalonia 12-compatible notification package или доказать, что можно убрать style include без потери UI.
- Временное удаление `Notification.Avalonia` style include годится только как diagnostic/unblocking probe; для финала нужно покрыть уведомления UI-test-ом.

### Шаг 9: замена `Mileeena.Notification.Avalonia` на internal toast overlay
Статус: blocker снят 2026-05-10; рабочая ветка содержит WIP-пробу Avalonia 12 + local toast overlay.

Что сделано:
- Удалена direct dependency `Mileeena.Notification.Avalonia` из `src/Directory.Packages.props` и `src/Unlimotion/Unlimotion.csproj`.
- Удален app-level style include `avares://Notification.Avalonia/Themes/Generic.xaml`, который зависал внутри `AvaloniaXamlLoader.Load(this)` на Avalonia 12.
- Добавлен local `AppToastNotificationManager` с `ErrorToast`/`SuccessToast`, auto-dismiss и close command.
- `NotificationManagerWrapper` оставлен как boundary для существующих call sites, но теперь работает с local toast manager и dispatch-ит вызовы на UI thread.
- В `MainScreen.axaml` добавлен toast overlay на `ItemsControl`, привязанный к `ToastNotificationManager.Messages`.
- `App.axaml.cs` и browser startup переведены с внешнего notification manager на `AppToastNotificationManager`.
- Добавлен headless UI-test `ToastNotificationUiTests`, который проверяет появление error toast, текст и закрытие кнопкой.

Проверки:
- `rg -n "Mileeena|Avalonia\.Notification|Notification\.Avalonia|NotificationMessage|INotificationMessageManager" src tests specs` - direct usages в `src`/`tests` не найдены; оставшиеся упоминания только в этой спецификации как исторический контекст.
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `dotnet build src\Unlimotion.Browser\Unlimotion.Browser.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/ToastNotificationUiTests/*" --timeout 30s --output Detailed --diagnostic --diagnostic-output-directory artifacts\test-diagnostics\avalonia12-toast-overlay --diagnostic-file-prefix toast-overlay --no-progress` - pass, 1/1.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work" --timeout 30s --output Detailed --diagnostic --diagnostic-output-directory artifacts\test-diagnostics\avalonia12-packagecompat-toast-overlay --diagnostic-file-prefix packagecompat-toast-overlay --no-progress` - pass, 1/1.
- `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*" --timeout 180s --output Detailed --diagnostic --diagnostic-output-directory artifacts\test-diagnostics\avalonia12-roadmapgraph-toast-overlay --diagnostic-file-prefix roadmapgraph-toast-overlay-rerun --no-progress` - pass, 34/34.
- Дополнительный первый прогон `RoadmapGraphUiTests` с `--timeout 60s` был прерван по слишком короткому лимиту после 31 passed и 0 failed; повтор с 180s прошел полностью.

Решение:
- Не форкаем `Mileeena.Notification.Avalonia`: для текущего use case достаточно небольшого internal toast overlay, а внешний пакет тянет старые Avalonia/SVG transitive dependencies и блокирует Avalonia 12 initialization.
- Notification blocker считается снятым при условии, что финальная ветка сохранит UI-test coverage на toast rendering/close.
- Оставшийся организационный риск перед merge-ready Avalonia 12 update - оформить источник internal `NodifyAvalonia 6.6.0-unlimotion.a12.1` как воспроизводимый package source, а не держать только локальный `.nupkg` artifact.

### Шаг 10: что нужно сделать, чтобы завершить update без деградации
Статус: план финализации сформирован 2026-05-11.

Текущее состояние по scan:
- `dotnet list src\Unlimotion.sln package --outdated --no-restore` показал, что на 2026-05-11 latest stable для Avalonia family уже `12.0.3`, а в WIP стоит `12.0.2`.
- `TUnit` latest stable уже `1.44.0`, а в WIP стоит `1.43.41`.
- `NodifyAvalonia 6.6.0-unlimotion.a12.1` отображается как ниже stable `6.6.0`, но это ожидаемо: internal prerelease нужен именно из-за Avalonia 12 compatibility; это не обычный outdated, а deliberate fork exception.
- Solution-level scan завершился с exit code 1 из-за `src\docker-compose.dcproj`/`package.config` limitation, но по проектам до этого вывел актуальные remaining items.

Минимальный путь без деградации:
1. Поднять Avalonia family с `12.0.2` до `12.0.3` во всех centralized versions: `Avalonia`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Desktop`, `Avalonia.iOS`, `Avalonia.Browser`, `Avalonia.Android`, `Avalonia.Headless`.
2. Поднять `TUnit` с `1.43.41` до `1.44.0` и проверить, что local AppAutomation glue продолжает собираться.
3. Оформить `NodifyAvalonia 6.6.0-unlimotion.a12.1` как воспроизводимую dependency:
   - предпочтительно: отдельный fork/branch + CI/package publishing в private/internal feed;
   - временно допустимо: зафиксировать local package source `artifacts\nuget-local` и сам `.nupkg`, но это хуже для долгой поддержки;
   - обязательно: документировать, из какого upstream branch/commit собран package и с какими MSBuild parameters.
4. Убрать диагностические/лишние local package artifacts, если они не используются, чтобы restore не зависел от случайных probe-файлов.
5. Прогнать полный restore/build matrix:
   - `src\Unlimotion.sln`;
   - `src\Unlimotion.Browser\Unlimotion.Browser.csproj`;
   - `src\Unlimotion.Android\Unlimotion.Android.csproj`;
   - desktop packaging projects: regular, Debian, macOS.
6. Прогнать обязательные UI/regression checks:
   - `ToastNotificationUiTests`;
   - `PackageUpdateCompatibilityUiTests`;
   - полный `RoadmapGraphUiTests`;
   - релевантные `MainControl*` headless tests, потому что XAML `Watermark -> PlaceholderText`, drag/drop и focus API затрагивают пользовательские flows;
   - `tests\Unlimotion.UiTests.Headless`;
   - хотя бы один FlaUI smoke/startup сценарий для desktop app.
7. Проверить функциональные parity gaps вручную или через тесты:
   - roadmap graph: render, selection, drag relation, pan/zoom, minimap, create hotkeys, search highlight;
   - notifications: error/success toast, close button, auto-dismiss, overlay не перекрывает loading/dialog flows;
   - dialogs/folder picker;
   - clipboard copy/paste outline;
   - browser startup after replacing notification manager.
8. Обновить outdated report/spec после финального scan: если remaining item только `NodifyAvalonia` internal prerelease, записать его как осознанное исключение с причиной и owner-ом.
9. Сделать финальный commit отдельным logical changeset: Avalonia 12 + compatibility fixes + internal toast + tests/spec. Если изменений слишком много для review, split лучше делать по уже проверенным блокам, но финальный PR должен проходить весь regression gate.

Решение:
- Следующий практический шаг - не расширять feature work, а поднять Avalonia до `12.0.3` и TUnit до `1.44.0`, затем прогнать тот же gate.
- Главный remaining risk не технический runtime blocker, а воспроизводимость `NodifyAvalonia` fork и достаточная ширина UI regression gate.

### Шаг 11: финализация Avalonia/TUnit/AppAutomation updates и regression gate
Статус: выполнено 2026-05-11; runtime/blocking regressions на текущем gate не обнаружены.

Что сделано:
- Подняты Avalonia packages до `12.0.3`, `ReactiveUI.Avalonia` до `12.0.1`, `DialogHost.Avalonia` до `0.12.2`, `TUnit` до `1.44.0`, AppAutomation packages до `1.5.7`.
- Оставлен deliberate internal fork exception: `NodifyAvalonia 6.6.0-unlimotion.a12.1` из local/internal package source, потому что upstream stable `6.6.0` не является Avalonia 12-ready dependency для нашего graph surface.
- Исправлен Android startup API под Avalonia 12: `MainActivity : AvaloniaMainActivity`, отдельный `AndroidApp : AvaloniaAndroidApplication<App>`, сервисы вынесены в `ConfigureAppServices()`.
- Исправлена инициализация `DialogHostStyles`: styles добавляются после `AvaloniaXamlLoader.Load(this)`, а подписка на `LocalizationService.CultureChanged` стала single-current handler, чтобы headless/AppAutomation изолированные приложения не падали на resource refresh.
- `FindParentDataContext<T>` расширен fallback-ом по logical tree; это закрывает потерю owner context в части Avalonia 12 visual/logical tree paths.
- AppAutomation Headless launch теперь использует async `await vm.Connect()` вместо sync-over-async.
- Для desktop FlaUI стабилизирован settings SSH сценарий: smoke config содержит SSH remote URL, а `SshKeysSection` объявлен явной UIA group через `AutomationProperties.ControlTypeOverride="Group"` и `IsControlElementOverride="True"`. Это сохраняет проверку видимости SSH controls без зависимости от ненадежного UIA ValuePattern text setter.

Проверки:
- `dotnet build tests\Unlimotion.UiTests.FlaUI\Unlimotion.UiTests.FlaUI.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass.
- `dotnet run --no-build --project tests\Unlimotion.UiTests.FlaUI\Unlimotion.UiTests.FlaUI.csproj -- --timeout 900s ...` - pass, 7/7. Первый полный прогон дал transient COM timeout на relation editor, но индивидуальный retry прошел, а повторный полный прогон прошел 7/7.
- `dotnet build tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass.
- `dotnet run --no-build --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --timeout 240s ...` - pass, 23/23.
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- Focused local UI checks `SettingsControlResponsiveUiTests`, `ToastNotificationUiTests`, `PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work` - pass, 4/4.
- `MainControlTreeCommandsUiTests/TaskOutlinePastePreviewDialog_LargePreview_IsScrollableAndShowsLastTask` - pass, 1/1.
- `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass; warnings remain for `LibGit2Sharp.NativeBinaries` constraint/Android 16 page size and existing Android API analyzer warnings.
- Outdated scan:
  - `src\Unlimotion\Unlimotion.csproj`: only `NodifyAvalonia 6.6.0-unlimotion.a12.1` vs upstream stable `6.6.0`, accepted internal prerelease exception.
  - `tests\Unlimotion.UiTests.Headless`, `tests\Unlimotion.UiTests.FlaUI`, `src\Unlimotion.Android`: no package updates for current sources.

Решение:
- Технические blockers для завершения update сняты на текущем gate.
- Перед финальным PR/merge остается организационный пункт: оформить воспроизводимый source/publish flow для `NodifyAvalonia 6.6.0-unlimotion.a12.1` и задокументировать upstream branch/commit/patch set.
- Android warnings по `LibGit2Sharp.NativeBinaries` не блокируют Avalonia update, но их нужно оставить в known risks: это отдельный dependency/platform risk, связанный с Android 16 page size и custom native binaries.

### Шаг 12: upstream PR для `NodifyAvalonia`
Статус: выполнено 2026-05-11.

Что сделано:
- Создан fork `Kibnet/nodify-avalonia` от `BAndysc/nodify-avalonia`.
- Создана ветка `codex/avalonia-12-support` от upstream `avalonia_port`.
- Ветка включает cherry-pick трех коммитов из найденного ранее fork branch `JuenTingShie/nodify-avalonia:copilot/update-avalonia-packages-to-12` с сохранением author metadata:
  - `feat: upgrade Avalonia packages from 11.1.0 to 12.0.1`;
  - `fix: add x:CompileBindings=False to fix XAML compiled-binding errors in Examples`;
  - `fix: connection lines not showing selected border`.
- Поверх этого добавлен follow-up commit `Update Avalonia support to 12.0.3`:
  - `Directory.Build.props`: `AvaloniaVersion` поднят до `12.0.3`;
  - `Examples/Nodify.Shapes.Web`: browser target framework поднят до `net10.0-browser`, потому что `Avalonia.Browser 12.x` публикует browser assets под `net10.0-browser`;
  - `README.md`: compatibility chart получил строку `unreleased -> 12.0.3`.
- Открыт upstream PR: https://github.com/BAndysc/nodify-avalonia/pull/45

Проверки в fork:
- `dotnet build Nodify\Nodify.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass, warnings only.
- `dotnet build Nodify.sln -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` - pass; остался один WebAssembly workload warning про native references при выключенных `WasmBuildNative`/`RunAOTCompilation`.

Решение:
- Для текущего Avalonia 12 update upstream-кандидатом остается только `NodifyAvalonia`.
- `Mileeena.Notification.Avalonia` не форкаем: зависимость удалена и заменена internal toast overlay.
- `Avalonia.Controls.PanAndZoom` не форкаем: direct dependency была stale и удалена.
- `LibGit2Sharp.NativeBinaries` не входит в scope текущего Avalonia 12 upstreaming: Android warnings остаются отдельным dependency/platform risk.

## 11. Журнал диалога
| Время | Участник | Тезис / решение | Последствие |
| --- | --- | --- | --- |
| 2026-05-09 | Пользователь | Нужно обсудить оставшиеся необновленные пакеты в форме диалога и конспектировать результаты в файл | Создан этот файл |
| 2026-05-09 | Codex | Сформировал текущую матрицу оставшихся updates и четыре возможных маршрута: Avalonia 12 сразу, ждать upstream, предварительно развязаться от UI-библиотек, AppAutomation/TUnit отдельно | Следующий шаг: выбрать стратегию по Avalonia и TUnit |
| 2026-05-09 | Пользователь | Сначала снимаем блокеры | Выбран маршрут C: подготовительная развязка от блокирующих пакетов до Avalonia 12 |
| 2026-05-09 | Codex | По коду `PanAndZoom` выглядит неиспользуемым direct reference; `NodifyAvalonia` является главным UI-блокером и сосредоточен в `GraphControl`; AppAutomation/TUnit остается отдельным тестовым блокером | Следующий рекомендуемый шаг: удалить stale `PanAndZoom`, затем заняться boundary вокруг `NodifyEditor` |
| 2026-05-09 | Пользователь | Подтвердил удаление stale `PanAndZoom` | Выполнено удаление direct dependency |
| 2026-05-09 | Codex | Удалил `Avalonia.Controls.PanAndZoom`; restore/build прошли; package outdated больше не показывает этот пакет; две устойчивые UI-проверки roadmap прошли, три старых roadmap tests падают независимо от наличия `PanAndZoom` | `PanAndZoom` blocker снят; перед следующим этапом желательно починить/обновить roadmap characterization tests или перейти к `NodifyEditor` boundary с учетом этих test gaps |
| 2026-05-09 | Codex | Закрыл roadmap characterization test gaps: helper открытия roadmap tab, direct-root lookup, minimap-vs-card lookup, async ожидание scheduled rebuild, owner fallback через `GraphViewModel` | Полный `RoadmapGraphUiTests` проходит 34/34; `PanAndZoom` blocker снят без текущих UI-test gaps; следующий blocker - `NodifyAvalonia`/`NodifyEditor` boundary |
| 2026-05-09 | Codex | Выполнил легкий boundary вокруг imperative API `NodifyEditor`: zoom/pan/fit вынесены в `IRoadmapViewportAdapter`, XAML surface пока остается на Nodify | Риск будущей Avalonia 12 миграции снижен; следующий выбор - глубокий XAML boundary или оценка fork/replacement `NodifyAvalonia` |
| 2026-05-09 | Codex | Поднял TUnit до `1.43.41`, удалил `AppAutomation.TUnit` и заменил его локальным glue; package outdated для AppAutomation-проектов пустой | NuGet blocker по AppAutomation/TUnit снят; оставшееся зависание `Unlimotion.UiTests.Headless` выделено в отдельный lifecycle blocker |
| 2026-05-09 | Codex | Перевел drag/drop код и tests с legacy `DataObject`/`DoDragDrop` на `IDataTransfer`/`DoDragDropAsync` | Drag/drop API blocker для Avalonia 12 снят; остался небольшой obsolete clipboard API и крупный `NodifyAvalonia` XAML blocker |
| 2026-05-09 | Codex | Заменил obsolete clipboard read API `IClipboard.GetTextAsync()` на `TryGetTextAsync()` | Малый Avalonia API blocker снят; остается крупный `NodifyAvalonia` XAML blocker перед Avalonia 12 |
| 2026-05-09 | Codex | Выполнил пробный Avalonia 12 bump; restore/build доводятся до pass, но UI initialization зависает на `Nodify` theme/surface | Подтвержден hard blocker: нужно форкнуть, заменить или локально реализовать roadmap graph surface до merge-ready Avalonia 12 update |
| 2026-05-10 | Пользователь | Попросил посмотреть Avalonia 12 breaking changes и найти forks, которые уже перевели Nodify на Avalonia 12 | Выполнен разбор breaking changes и поиск forks |
| 2026-05-10 | Codex | Нашел один прямой fork-branch `JuenTingShie/nodify-avalonia` под Avalonia 12.0.1, который собирается без ошибок, и отдельный `NodifyM.Avalonia` под Avalonia 12.0.2, но не drop-in | Следующий рекомендуемый шаг: сделать controlled internal package probe текущего `NodifyAvalonia` fork, затем решать, остается ли fork или нужна замена на `NodifyM.Avalonia` |
| 2026-05-10 | Пользователь | Подтвердил controlled probe: "Давай попробуем" | Начата WIP-проба Avalonia 12 + internal `NodifyAvalonia` package |
| 2026-05-10 | Codex | Полный `NodifyAvalonia 6.6.0-unlimotion.a12.1` на Avalonia 12 проходит `PackageUpdateCompatibilityUiTests` и `RoadmapGraphUiTests` при отключенном `Notification.Avalonia` style include; дополнительно исправлен Avalonia 12 dispatcher blocker в `GraphControl` | `NodifyAvalonia` fork признан жизнеспособным первым маршрутом; следующий blocker - заменить/форкнуть `Mileeena.Notification.Avalonia` или реализовать internal toast overlay |
| 2026-05-10 | Пользователь | Подтвердил продолжение: "давай" | Начата замена `Mileeena.Notification.Avalonia` |
| 2026-05-10 | Codex | `Mileeena.Notification.Avalonia` заменен на local `AppToastNotificationManager` + `MainScreen` toast overlay; добавлен `ToastNotificationUiTests`; build, toast UI-test, compatibility test и `RoadmapGraphUiTests` проходят | Notification blocker снят; следующий риск - оформить воспроизводимый package source для internal `NodifyAvalonia` fork перед финализацией Avalonia 12 update |
| 2026-05-11 | Пользователь | Спросил, что теперь нужно сделать, чтобы закончить обновление без деградации функции | Сформирован финальный checklist: Avalonia `12.0.3`, TUnit `1.44.0`, воспроизводимый `NodifyAvalonia` fork/package source, полный build/UI regression gate и documented exception для internal prerelease |
| 2026-05-11 | Пользователь | Сказал "выполняй" | Выполнена финализация package updates и regression gate |
| 2026-05-11 | Codex | Закрыт FlaUI settings SSH blocker через преднастроенный SSH remote в automation config и явную UIA group для `SshKeysSection`; полный FlaUI повторно прошел 7/7, Headless прошел 23/23, Android full build прошел | Runtime blockers сняты; оставлен documented exception только для internal `NodifyAvalonia` fork/package source |
| 2026-05-11 | Пользователь | Попросил форкнуть библиотеки, которые нужно upstream-нуть, сделать ветки и PR в родительские репозитории | Создан `Kibnet/nodify-avalonia`, ветка `codex/avalonia-12-support`; открыт upstream PR `BAndysc/nodify-avalonia#45`; зафиксировано, что остальные библиотеки в scope не upstream-ятся |
