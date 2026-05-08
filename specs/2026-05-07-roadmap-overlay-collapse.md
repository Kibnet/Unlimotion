# Roadmap overlay collapse controls

## 0. Метаданные
- Тип (профиль): delivery-task; stack profile `dotnet-desktop-client`; overlay profile `ui-automation-testing`.
- Владелец: Unlimotion desktop UI.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая ветка `fix/roadmap-usage`.
- Ограничения: центральный `QUEST` требует SPEC-first и подтверждение фразой `Спеку подтверждаю` до изменений кода; локальный override требует UI tests для UI-facing изменений.
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Projects\My\Agents\AGENTS.md`, `C:\Projects\My\Agents\instructions\governance\routing-matrix.md`, `C:\Projects\My\Agents\instructions\core\model-behavior-baseline.md`, `C:\Projects\My\Agents\instructions\core\quest-governance.md`, `C:\Projects\My\Agents\instructions\core\quest-mode.md`, `C:\Projects\My\Agents\instructions\core\collaboration-baseline.md`, `C:\Projects\My\Agents\instructions\core\testing-baseline.md`, `C:\Projects\My\Agents\instructions\contexts\testing-dotnet.md`, `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`, `C:\Projects\My\Agents\instructions\profiles\ui-automation-testing.md`.

## 1. Overview / Цель
Добавить возможность вручную сворачивать overlay-элементы roadmap-графа: миникарту и панель управления viewport. В свернутом состоянии каждый overlay заменяется маленькой пиктограммой-кнопкой в своем углу, чтобы на узких экранах они не закрывали большую часть графа и не мешали друг другу.

Outcome contract:
- Success means: пользователь может отдельно свернуть миникарту и панель управления графом, увидеть вместо них компактные кнопки, развернуть каждую обратно и продолжить использовать существующие zoom/pan/minimap действия.
- Итоговый артефакт / output: изменения `GraphControl.axaml` / `GraphControl.axaml.cs`, regression UI coverage в `RoadmapGraphUiTests`, обновленная рабочая спека с журналом.
- Stop rules: не менять алгоритм построения roadmap, не переносить состояние в persisted settings, не менять существующие shortcut/drag/drop/task action сценарии.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion/Views/GraphControl.axaml` содержит `nodify:NodifyEditor` и два постоянных overlay:
  - `RoadmapViewportToolbar` слева снизу с кнопками zoom, fit, reset и pan;
  - `RoadmapMinimapPanel` справа снизу с `nodify:Minimap`.
- Оба overlay имеют фиксированные/контентные размеры и всегда видимы, если открыт roadmap.
- На узком экране миникарта `260x170` и toolbar визуально занимают значимую часть нижней области графа; при малой ширине они могут перекрывать область просмотра и мешать взаимодействию с картой.
- В `GraphControl.axaml.cs` уже есть code-behind handlers для viewport-кнопок и миникарты, поэтому локальное UI-only состояние этого control соответствует текущей архитектуре.
- В `RoadmapGraphUiTests` уже есть test `RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls`, который проверяет наличие overlay и работоспособность zoom/pan/reset.

## 3. Проблема
Roadmap overlay-элементы нельзя убрать с рабочей области: на узких экранах миникарта и панель управления закрывают значительную часть графа, а пользователь не может освободить пространство без потери возможности вернуть эти инструменты.

## 4. Цели дизайна
- Разделение ответственности: хранить только transient UI-state внутри `GraphControl`, не загрязняя `GraphViewModel` и persisted settings.
- Повторное использование: сохранить существующие zoom/pan/minimap handlers и automation-id существующих controls.
- Тестируемость: добавить Avalonia.Headless regression test на свернуть/развернуть и на сохранение работоспособности overlay после разворачивания.
- Консистентность: поведение должно быть одинаковым на широком и узком экране; по умолчанию текущий desktop layout остается развернутым.
- Обратная совместимость: существующие automation-id `RoadmapViewportToolbar`, `RoadmapMinimapPanel`, `RoadmapMinimap`, `RoadmapZoomBorder` и кнопок управления не переименовывать.

## 5. Non-Goals (чего НЕ делаем)
- Не делать автоматическое сворачивание по breakpoint в рамках этой задачи.
- Не сохранять состояние свернутости между перезапусками.
- Не менять размеры roadmap nodes, layout-алгоритм, фильтры или rendering roadmap nodes/connections.
- Не менять уже исправленные task actions: click, double-click, drag/drop, hotkeys и правый pan.
- Не добавлять новую зависимость на icon pack; использовать существующие Avalonia controls и локальные стили.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/GraphControl.axaml` -> разметка expanded/collapsed состояний для toolbar и minimap, кнопки collapse/expand, stable automation-id.
- `src/Unlimotion/Views/GraphControl.axaml.cs` -> два UI-only `StyledProperty<bool>` с default `true` и click handlers для переключения состояний.
- `src/Unlimotion.Test/RoadmapGraphUiTests.cs` -> UI-тесты на narrow viewport и проверка сохранения existing overlay actions.
- `specs/2026-05-07-roadmap-overlay-collapse.md` -> рабочая спецификация и журнал.

### 6.2 Детальный дизайн
- Добавить в `GraphControl` свойства:
  - `IsRoadmapViewportToolbarExpanded`, default `true`;
  - `IsRoadmapMinimapExpanded`, default `true`.
- В expanded состоянии:
  - `RoadmapViewportToolbar` остается слева снизу и содержит текущие zoom/pan кнопки плюс маленькую collapse-кнопку `RoadmapViewportToolbarCollapseButton`;
  - `RoadmapMinimapPanel` остается справа снизу и содержит текущий `RoadmapMinimap` плюс маленькую collapse-кнопку `RoadmapMinimapCollapseButton`;
  - collapse-кнопки внутри expanded panels должны размещаться как overlay в углу панели или другим способом, который не добавляет новый широкий layout-slot и не увеличивает footprint панели настолько, чтобы ухудшить narrow-screen overlap;
  - `RoadmapMinimapPanel` должен сохранить текущие `Width=260` и `Height=170`, чтобы collapse-кнопка не расширяла fixed minimap overlay.
- В collapsed состоянии:
  - соответствующий expanded `Border` получает `IsVisible=false`;
  - в том же углу показывается compact button:
    - `RoadmapViewportToolbarExpandButton` слева снизу;
    - `RoadmapMinimapExpandButton` справа снизу.
- Compact buttons должны быть стабильного малого размера: ориентир `32-40px` по ширине и высоте.
- Existing viewport/minimap binding не пересоздавать вручную; XAML visibility достаточно скрывает/возвращает controls.
- Output/evidence rules:
  - collapse action должен менять только видимость overlay, а не `ViewportZoom`, `ViewportLocation`, выбранную задачу или фильтры;
  - после expand существующие кнопки zoom/pan/reset должны работать как раньше;
  - после expand `RoadmapMinimap` должен снова быть видимым и оставаться связанным с `RoadmapEditor.ViewportLocation` / `ViewportSize`;
  - automation-id должны позволять UI tests найти expanded panel, collapse button, expand button и существующие controls.
- Производительность: изменение visibility не должно запускать rebuild roadmap; state локальный и не подписывается на task data.

## 7. Бизнес-правила / Алгоритмы
- Каждый overlay сворачивается независимо.
- Свернутая миникарта не должна скрывать toolbar и наоборот; compact buttons остаются в разных углах.
- По умолчанию оба overlay развернуты, чтобы сохранить текущее поведение для existing users и tests.
- Разворачивание одного overlay не меняет состояние второго.

## 8. Точки интеграции и триггеры
- `RoadmapViewportToolbarCollapseButton.Click` -> `IsRoadmapViewportToolbarExpanded = false`.
- `RoadmapViewportToolbarExpandButton.Click` -> `IsRoadmapViewportToolbarExpanded = true`.
- `RoadmapMinimapCollapseButton.Click` -> `IsRoadmapMinimapExpanded = false`.
- `RoadmapMinimapExpandButton.Click` -> `IsRoadmapMinimapExpanded = true`.
- Existing `RoadmapZoomIn_OnClick`, `RoadmapZoomOut_OnClick`, `RoadmapFit_OnClick`, `RoadmapResetViewport_OnClick`, `RoadmapPan*` and `RoadmapMinimap_OnZoom` remain unchanged.

## 9. Изменения модели данных / состояния
- Новые поля: два `StyledProperty<bool>` в `GraphControl`.
- Persisted vs calculated: transient UI-only state; не сохраняется в настройках и не попадает в `GraphViewModel`.
- Влияние на хранилище: отсутствует.

## 10. Миграция / Rollout / Rollback
- Первый запуск после обновления: оба overlay открыты как раньше.
- Обратная совместимость: существующие tests/selectors для overlay должны продолжить находить expanded controls до collapse.
- Rollback: откатить изменения в `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs` и эту спецификацию.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. На roadmap доступны отдельные collapse-кнопки для `RoadmapViewportToolbar` и `RoadmapMinimapPanel`.
  2. После collapse toolbar скрывается, вместо него видна маленькая кнопка `RoadmapViewportToolbarExpandButton`.
  3. После collapse minimap скрывается, вместо нее видна маленькая кнопка `RoadmapMinimapExpandButton`.
  4. Свертывание одного overlay не меняет состояние второго.
  5. Нажатие expand-кнопки возвращает соответствующий overlay.
  6. После expand существующие zoom/pan/reset кнопки продолжают менять `ViewportZoom` / `ViewportLocation`.
  7. После expand minimap снова видна и синхронизирована с viewport editor bindings.
  8. На narrow viewport compact buttons остаются видимыми и имеют малые bounds, не превращаясь в крупные панели.
  9. Existing roadmap task interactions и overlay test не регрессируют.
- Какие тесты добавить/изменить:
  - Добавить Avalonia.Headless test в `RoadmapGraphUiTests`, например `RoadmapGraph_ViewportOverlays_CollapseToCompactButtonsAndRestore`.
  - Тест создать окно узкой ширины через overload `CreateWindow(Control content, double width, double height)`.
  - Тест должен нажимать collapse/expand реальным headless mouse-level кликом через `Window.MouseDown` / `Window.MouseUp` по координатам control, а не только `RaiseEvent`, чтобы поймать z-order/overlap проблемы.
  - После collapse minimap тест должен реальным кликом проверить, что toolbar collapse/expand или toolbar action доступны и не перекрыты collapsed/expanded minimap layer.
  - Тест найти controls по automation-id, нажать collapse/expand, проверить `IsVisible`, размеры compact buttons и работоспособность `RoadmapZoomInButton`/`RoadmapPanRightButton` после восстановления.
  - Тест должен проверить minimap после expand: `RoadmapMinimap` снова visible, а его binding к editor не потерян; минимально допустимо изменить `RoadmapEditor.ViewportLocation` и проверить, что bound `ViewportLocation` на minimap получил то же значение через reflection, если headless minimap zoom gesture неудобен.
  - При необходимости обновить `RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls`, чтобы он учитывал новые collapse buttons, но не ослаблял существующие проверки.
- Characterization tests:
  - Existing `RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls` должен продолжить подтверждать исходно развернутое состояние.
- Команды для проверки:
  - `dotnet build src\Unlimotion\Unlimotion.csproj --no-restore -v:minimal`
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj --no-restore -v:minimal`
  - `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"`
  - Полный test run проекта перед финалом: `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj`
- Stop rules для validation:
  - если targeted UI tests падают по новой функциональности, не завершать задачу;
  - если full run падает на unrelated known issue, зафиксировать конкретный failing test и evidence targeted pass.

## 12. Риски и edge cases
- Collapse-кнопка внутри minimap может перекрыть часть minimap interaction. Смягчение: маленькая кнопка в углу с минимальным размером.
- Скрытие controls через `IsVisible=false` может ломать тесты, если они ищут hidden controls как visible. Смягчение: тестировать до/после collapse явно, existing test оставляет default expanded path.
- XAML binding на negation `!IsRoadmapMinimapExpanded` должен соответствовать существующему стилю проекта, где уже используется `{Binding !OnlyUnlocked}`.
- Если collapse-кнопки добавляются как обычные children в layout, они могут увеличить expanded overlay footprint. Смягчение: размещать их overlay-слоем или фиксировать bounds так, чтобы minimap не меняла `260x170`, а toolbar не становился шире из-за отдельного layout-slot.
- Не следует добавлять auto-collapse по width без отдельного решения: это меняет initial behavior и может удивить desktop users.

## 13. План выполнения
1. Добавить regression UI test на narrow viewport, который воспроизводит отсутствие collapse controls до фикса.
2. Добавить `StyledProperty` state и click handlers в `GraphControl.axaml.cs`.
3. Обновить `GraphControl.axaml`: expanded panels с collapse buttons и collapsed compact buttons.
4. Запустить targeted build/UI tests, затем полный test run или явно зафиксировать unrelated blocker.
5. Выполнить post-EXEC review и обновить журнал спеки.

## 14. Открытые вопросы
Нет блокирующих вопросов. Выбран manual collapse без auto-breakpoint, потому что запрос формулирует именно возможность сворачивания, а сохранение текущего default layout снижает риск регрессий.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`.
- Выполненные требования профиля:
  - UI-state остается в desktop control, без блокировок UI-потока.
  - Изменение пользовательского flow будет покрыто Avalonia.Headless UI test.
  - Stable automation-id сохраняются и добавляются для новых buttons.
  - План включает build, targeted UI tests и full test run.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/GraphControl.axaml` | Добавить collapse/expand buttons и visibility bindings для roadmap toolbar/minimap | Освободить область графа на узких экранах |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Добавить UI-only expanded properties и click handlers | Управление transient состоянием overlay |
| `src/Unlimotion.Test/RoadmapGraphUiTests.cs` | Добавить narrow viewport regression UI test | Подтвердить collapse/expand behavior и отсутствие регрессии overlay actions |
| `specs/2026-05-07-roadmap-overlay-collapse.md` | Рабочая спецификация и журнал | QUEST gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Roadmap toolbar | Всегда крупный overlay слева снизу | Можно свернуть в маленькую кнопку и развернуть обратно |
| Roadmap minimap | Всегда `260x170` overlay справа снизу | Можно свернуть в маленькую кнопку и развернуть обратно |
| Narrow viewport | Overlay закрывают значимую часть графа | Пользователь освобождает область графа вручную |
| State ownership | Нет collapse state | Local transient state в `GraphControl` |
| UI tests | Проверяют наличие overlay и actions | Дополнительно проверяют collapse/expand на narrow viewport |

## 18. Альтернативы и компромиссы
- Вариант: автоматическое сворачивание по ширине окна.
- Плюсы: сразу решает narrow-screen clutter без действия пользователя.
- Минусы: меняет initial behavior, требует breakpoint contract и дополнительных edge cases при resize.
- Почему выбранное решение лучше в контексте этой задачи: пользователь запросил возможность сворачивать; manual collapse минимален, обратимо сохраняет desktop default и не требует persisted/responsive policy.

- Вариант: перенести состояние в `GraphViewModel`.
- Плюсы: легче тестировать как VM state, потенциально можно сохранять.
- Минусы: UI-only concern попадет в graph data VM и расширит публичный surface без необходимости.
- Почему выбранное решение лучше в контексте этой задачи: состояние не доменное и не persisted; `GraphControl` уже владеет viewport UI actions.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, state ownership, rollout и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Есть acceptance criteria, edge cases и пошаговый план. |
| D. Проверяемость | 14-16 | PASS | Указаны UI tests, build/test commands и file map. |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет блокирующих вопросов, компромиссы описаны. |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation требования отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Сформулирована одна UI-проблема и явные Non-Goals. |
| 2. Понимание текущего состояния | 5 | Зафиксированы существующие overlay, code-behind handlers и tests. |
| 3. Конкретность целевого дизайна | 5 | Описаны свойства, кнопки, automation-id и visibility contract. |
| 4. Безопасность (миграция, откат) | 5 | State transient, default unchanged, rollback прост. |
| 5. Тестируемость | 5 | Есть targeted UI regression и команды проверки. |
| 6. Готовность к автономной реализации | 5 | Нет открытых вопросов, план линейный и ограниченный. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлен явный отказ от auto-collapse, чтобы не менять initial behavior; acceptance criteria усилены проверкой независимости двух overlay и сохранения viewport actions после expand; после review уточнены mouse-level UI checks, minimap binding evidence и запрет увеличивать footprint expanded panels обычным layout-slot.
- Что осталось на решение пользователя: подтвердить спецификацию фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: добавлены local `GraphControl` expanded-state properties, compact expand buttons и overlay collapse buttons без изменения fixed minimap size; добавлен mouse-level narrow viewport UI regression test; после clean/rebuild устранено stale Avalonia XAML test output состояние.
- Что проверено дополнительно для refactor / comments: новых комментариев нет; state не вынесен в `GraphViewModel`; existing automation-id сохранены; `git diff --check` без ошибок.
- Остаточные риски / follow-ups: полный `RoadmapGraphUiTests` class run завис на таймауте 6 минут без вывода; отдельные затронутые UI-сценарии и smoke-тесты пройдены.

## Approval
Подтверждено пользователем фразой: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор instruction stack и UI-контекста | 0.95 | Нет | Создать рабочую спецификацию | Нет | Нет | Центральный QUEST требует SPEC-first; локальный override требует UI coverage. Осмотрены `GraphControl` overlay и существующий `RoadmapGraphUiTests` | `AGENTS.md`, `AGENTS.override.md`, central instructions, `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
| SPEC | Проектирование collapse UX | 0.9 | Нет | Запросить подтверждение спеки | Да, перед EXEC | Да, ожидается подтверждение | Выбран manual collapse с default expanded: это прямо решает запрос на возможность свернуть и не меняет начальное desktop поведение | `specs/2026-05-07-roadmap-overlay-collapse.md` |
| SPEC | Исправление review-находок | 0.95 | Нет | Запросить подтверждение спеки | Да, перед EXEC | Да, пользователь попросил исправить review findings | Уточнены тесты до mouse-level clicks, добавлена проверка восстановленной minimap binding и снято противоречие про размеры: запрещено менять размеры roadmap nodes/layout, а overlay-кнопки не должны увеличивать footprint панелей | `specs/2026-05-07-roadmap-overlay-collapse.md` |
| EXEC | Подтверждение спеки и regression test | 0.9 | Нет | Реализовать collapse state | Нет | Да, пользователь подтвердил фразой `Спеку подтверждаю` | Добавлен Avalonia.Headless тест на narrow viewport: реальные mouse clicks по collapse/expand, compact button bounds, toolbar action после скрытия minimap и восстановление minimap binding | `RoadmapGraphUiTests.cs` |
| EXEC | Реализация overlay collapse | 0.92 | Нет | Запустить build и targeted tests | Нет | Нет | В `GraphControl` добавлены transient `StyledProperty` для expanded states, compact buttons и overlay collapse buttons; minimap сохранил fixed `260x170`, existing viewport handlers переиспользованы | `GraphControl.axaml`, `GraphControl.axaml.cs` |
| EXEC | Верификация | 0.86 | Полный class run завис на 6-минутном timeout | Post-EXEC review и финальный статус | Нет | Нет | `dotnet build` app/test прошли после clean/rebuild; новый overlay test, existing overlay test, node right-drag pan и node click/double-tap tests прошли. Полный `RoadmapGraphUiTests/*` завис без вывода, поэтому полный project run не запускался: он включает тот же зависающий class path | build/test commands, `RoadmapGraphUiTests.cs` |
