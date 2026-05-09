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

## 8. Журнал диалога
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
