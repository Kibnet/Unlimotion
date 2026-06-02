# Roadmap search must not rebuild the map

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущий worktree `HEAD (no branch)`
- Ограничения: до подтверждения менять только этот spec; после подтверждения не менять публичный API и не трогать unrelated files
- Связанные ссылки: Не применимо; локальный bugfix без внешнего issue

## 1. Overview / Цель
Поиск на вкладке roadmap должен только обновлять выделение подходящих задач (`IsHighlighted`) и не должен менять набор задач, из которого строится карта.

Outcome contract:
- Success means: изменения `Search.SearchText` и `Search.IsFuzzySearch` на открытой карте не увеличивают `RoadmapGraphUpdateCount` и `RoadmapGraphBackgroundBuildStartCount` после стабилизации initial build, но matching node корректно подсвечивается по текущему exact/fuzzy режиму.
- Итоговый артефакт / output: правка ViewModel wiring, подписки `GraphControl` на search state и regression UI test.
- Stop rules: остановиться, если тест покажет, что текущий search нужен для состава roadmap roots по отдельному продуктовому контракту; иначе довести до targeted UI test, build и full test command либо явно зафиксировать объективный blocker.

## 2. Текущее состояние (AS-IS)
- `MainWindowViewModel` создает `searchTopFilter` для обычных списков задач.
- `emojiRootFilter` дополнительно подписан на `Search.SearchText` и при непустом поиске меняет root predicate: без активных emoji-фильтров возвращает `true` для всех задач.
- `ActivateRoadmapProjection()` использует `emojiRootFilter` для `Graph.Tasks`; из-за этого search может менять `Graph.Tasks`, а `GraphControl` видит collection change и пересобирает карту.
- `GraphControl` уже имеет отдельный путь для поиска: подписка на `dc.Search.SearchText` вызывает `UpdateHighlights()`, где меняется только `TaskItemViewModel.IsHighlighted`.
- `UpdateHighlights()` уже читает `Search.IsFuzzySearch`, но `GraphControl` не подписан на изменение этого флага; поэтому переключение fuzzy mode может не обновить подсветку до следующего изменения search text или другого refresh-trigger.
- `src/Unlimotion.Test/RoadmapGraphUiTests.cs` уже содержит `RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode()` и счетчики rebuild/update.

## 3. Проблема
Одна корневая проблема: roadmap смешивает search state с разными механизмами обновления: `Search.SearchText` может менять root collection через ViewModel filter, а `Search.IsFuzzySearch` не является полноценным refresh-trigger для highlight-only state.

## 4. Цели дизайна
- Разделение ответственности: filters формируют набор задач карты; search только подсвечивает.
- Повторное использование: сохранить существующий `emojiRootFilter` для обычного дерева, где search может влиять на отображаемые roots.
- Тестируемость: закрепить поведение через существующий Avalonia.Headless UI test с build counters.
- Консистентность: не менять UX подсветки, selection и styling.
- Обратная совместимость: не менять persisted state, public API, automation ids и поведение non-roadmap вкладок.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем алгоритм `RoadmapGraphBuilder`.
- Не меняем визуальный стиль подсветки.
- Не меняем search behavior для обычных task tabs.
- Не добавляем новые filters/settings.
- Не создаем новые automation ids или публичные API.
- Не коммитим video artifacts; если runner не пишет видео, фиксируем fallback evidence.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs` -> разделить root filtering для обычных task trees и roadmap.
- `src/Unlimotion/Views/GraphControl.axaml.cs` -> пересчитывать highlights при изменении `Search.SearchText` или `Search.IsFuzzySearch`, не вызывая `UpdateGraph()`.
- `src/Unlimotion.Test/RoadmapGraphUiTests.cs` -> добавить regression assertion, что search highlight не вызывает rebuild.

### 6.2 Детальный дизайн
- В `MainWindowViewModel` оставить текущий `emojiRootFilter` для `CurrentAllTasksItems`.
- Для roadmap projection добавить отдельный observable predicate, например `roadmapRootFilter`, который не подписан на `Search.SearchText` и сохраняет root selection по emoji filters как в empty-search ветке текущего `emojiRootFilter`.
- В `ActivateRoadmapProjection()` заменить `.Filter(emojiRootFilter)` на `.Filter(roadmapRootFilter)` только для `Graph.Tasks`.
- `Graph.UnlockedTasks` остается без search filter, как сейчас.
- `GraphControl.UpdateHighlights()` остается источником search-driven UI state.
- Подписку `GraphControl` на search state расширить с `Search.SearchText` до пары `Search.SearchText` + `Search.IsFuzzySearch`, чтобы переключение fuzzy mode пересчитывало highlight без rebuild.
- Инвариант no-rebuild относится к search text changes и matching-mode state (`Search.IsFuzzySearch`) как к состоянию поиска. Regression assertion покрывает оба триггера.
- Visual planning artifact: Не применимо как отдельный render; layout и flow не меняются. Fallback state схема:
  - до search: roadmap topology stable, nodes not highlighted unless previous query remains;
  - after search text: same nodes/connections/viewport, matching nodes get highlighted style;
  - after clear: same topology, highlight cleared.
- UI test video evidence: fallback during EXEC, потому что выбранный быстрый regression path - Avalonia.Headless unit UI test; если runner не сохраняет видео, evidence будет test output + counters.
- Обработка ошибок: не добавляется; существующие subscriptions и throttling остаются.
- Производительность: search больше не должен запускать expensive background roadmap build.

## 7. Бизнес-правила / Алгоритмы
- Search query:
  - не меняет `Graph.Tasks` / `Graph.UnlockedTasks`;
  - не вызывает `ScheduleUpdateGraph()` через collection changes;
  - вызывает только highlight recalculation.
- `Search.IsFuzzySearch`:
  - не должен подключаться к roadmap root/topology filters;
  - должен пересчитывать `IsHighlighted` для уже открытой карты без изменения `Graph.Tasks` / `Graph.UnlockedTasks`;
  - не должен вызывать `ScheduleUpdateGraph()` или background build.
- Emoji include/exclude filters:
  - продолжают менять набор задач roadmap;
  - продолжают вызывать rebuild.
- Title/description/emoji changes:
  - сохраняют существующее поведение highlight refresh и conditional rebuild.

## 8. Точки интеграции и триггеры
- `MainWindowViewModel.ActivateRoadmapProjection()` обязан использовать search-independent root filter.
- `GraphControl.GraphControl_DataContextChanged()` подписывает `Search.SearchText` и `Search.IsFuzzySearch` на `UpdateHighlights()`.
- Пересчет карты остается за `ScheduleUpdateGraph()` и collection/property triggers, которые реально меняют roadmap topology.

## 9. Изменения модели данных / состояния
- Новых полей persisted state нет.
- Новых calculated fields нет.
- Влияния на хранилище нет.

## 10. Миграция / Rollout / Rollback
- Первый запуск: поведение применяется сразу, миграции нет.
- Совместимость: файлы задач, настройки и UI selectors не меняются.
- Rollback: вернуть roadmap projection на прежний `emojiRootFilter` и удалить regression assertion.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - на открытой roadmap вкладке ввод search text подсвечивает matching node;
  - selected node остается selected;
  - `RoadmapGraphUpdateCount` не увеличивается после ввода search text;
  - `RoadmapGraphBackgroundBuildStartCount` не увеличивается после ввода search text;
  - очистка search снимает highlight и тоже не запускает rebuild;
  - переключение `Search.IsFuzzySearch` обновляет highlight для fuzzy-only query и не запускает rebuild;
  - счетчики фиксируются только после `WaitForStableRoadmapUpdates()` или эквивалентной стабилизации initial roadmap build;
  - отрицательная проверка ждет окно больше debounce/rebuild throttle после set, fuzzy toggle and clear, чтобы поймать поздний rebuild;
  - emoji filter changes still rebuild roadmap.
- Какие тесты добавить/изменить:
  - расширить `RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode()` или добавить соседний test в `RoadmapGraphUiTests`.
- Characterization tests / contract checks:
  - сначала добавить assertion и запустить targeted test на текущем коде, ожидая fail из-за rebuild.
- Детали regression assertion:
  - открыть roadmap tab и дождаться stable roadmap build;
  - сохранить `RoadmapGraphUpdateCount`, `RoadmapGraphBackgroundBuildStartCount`, node count/connection count и selected node reference;
  - установить `vm.Graph.Search.SearchText = targetTask.OnlyTextTitle`;
  - дождаться `targetTask.IsHighlighted == true`;
  - выполнить negative wait, подтверждающий отсутствие increment в обоих build counters;
  - для fuzzy режима задать deterministic title, например `"Roadmap fuzzy match target"`, установить typo query вроде `"fuzxy"` при `IsFuzzySearch = false`, проверить отсутствие highlight, затем `IsFuzzySearch = true`, дождаться highlight и выполнить negative wait по counters;
  - очистить search, дождаться `IsHighlighted == false` и повторить negative wait;
  - дополнительно проверить, что selected node reference и topology counts не изменились.
- Visual acceptance:
  - в headless UI test проверить bold/blue highlighted state и stable selection; screenshot/video не обязателен при отсутствии runner video support.
- UI video evidence:
  - fallback: `dotnet run`/`dotnet test` output targeted UI test; video не применимо, если Avalonia.Headless test harness не пишет видео artifacts.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode"`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`
- Stop rules для validation:
  - если targeted test не воспроизводит rebuild, сначала найти актуальный воспроизводимый UI test path, не править вслепую;
  - если full test command блокируется окружением, зафиксировать точную ошибку и выполнить next-best targeted suite.

## 12. Риски и edge cases
- Риск: ordinary task tree search мог случайно полагаться на общий `emojiRootFilter`; mitigation: не менять его, добавить новый roadmap-only filter.
- Риск: search toggle could still trigger rebuild via another subscription; mitigation: assert both update and background build counters.
- Риск: clearing search может иметь отдельный path; mitigation: assert no rebuild on set and clear.
- Риск: active emoji filters should still rebuild; mitigation: existing `RoadmapGraph_FilterChange_RebuildsOpenView()` remains as guard.
- Риск: fuzzy-mode assertion could accidentally match exact search; mitigation: use a deterministic typo query that `source.Contains(term)` cannot satisfy but `FuzzyMatcher.IsFuzzyMatch` should match.

## 13. План выполнения
1. Добавить failing regression assertions around search in `RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode()`, including stable initial counters, fuzzy toggle refresh and negative wait windows.
2. Запустить targeted test и подтвердить fail на текущем поведении.
3. Ввести roadmap-only root filter в `MainWindowViewModel` и подключить его в `ActivateRoadmapProjection()`.
4. Повторить targeted UI test.
5. Запустить `dotnet build src/Unlimotion.sln`.
6. Запустить `dotnet test src/Unlimotion.sln` или зафиксировать объективный blocker и next-best coverage.
7. Выполнить post-EXEC review-loop и исправить однозначные findings.

## 14. Открытые вопросы
Нет блокирующих вопросов. Требуется только approval gate по правилам QUEST.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`, context `testing-dotnet`
- Выполненные требования профиля:
  - UI behavior покрывается Avalonia.Headless UI test.
  - Automation selectors не меняются.
  - План включает targeted UI test, build и full test command.
  - Video evidence имеет объективный fallback для headless runner.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Добавить roadmap-only root filter без search dependency и использовать его для `Graph.Tasks` | Search не должен менять task set карты |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Подписать highlight refresh на `Search.SearchText` и `Search.IsFuzzySearch` | Fuzzy toggle должен менять подсветку без rebuild |
| `src/Unlimotion.Test/RoadmapGraphUiTests.cs` | Добавить regression assertion по counters и highlight | Зафиксировать UI contract и предотвратить повторную регрессию |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Roadmap search | Может менять `Graph.Tasks` через `emojiRootFilter` и запускать rebuild | Меняет только `IsHighlighted` |
| Roadmap fuzzy search | `UpdateHighlights()` учитывает `IsFuzzySearch`, но fuzzy toggle не является самостоятельным trigger | Fuzzy toggle пересчитывает `IsHighlighted` без rebuild |
| Roadmap topology | Может зависеть от search text | Зависит от task relations и active roadmap filters, но не от search text |
| UI test | Проверяет highlight/clear | Проверяет exact highlight/clear, fuzzy toggle refresh, stable selection/topology and absence of rebuild after set/toggle/clear |

## 18. Альтернативы и компромиссы
- Вариант: игнорировать search changes внутри `GraphControl`.
- Плюсы: минимальная локальная правка в control.
- Минусы: `Graph.Tasks` все равно меняется от search, что ломает contract модели и может влиять на другие consumers.
- Почему выбранное решение лучше: root cause находится в ViewModel projection wiring; roadmap должен получать search-independent collection, а `GraphControl` уже имеет правильный highlight-only механизм.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заданы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, integration triggers, perf и rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Нет данных/миграций; план rollback и границы изменения есть |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria cover exact search, fuzzy toggle, no-rebuild counters and validation commands |
| E. Готовность к автономной реализации | 17-19 | PASS | План и alternatives есть; blockers отсутствуют |
| F. Соответствие профилю | 20 | PASS | UI test coverage и validation requirements учтены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Search должен быть highlight-only для roadmap |
| 2. Понимание текущего состояния | 5 | Указаны `emojiRootFilter`, `Graph.Tasks`, `UpdateHighlights()` и счетчики |
| 3. Конкретность целевого дизайна | 5 | Определены roadmap-only root filter и search-state subscription в `GraphControl` |
| 4. Безопасность (миграция, откат) | 5 | Нет persistence changes; rollback простой |
| 5. Тестируемость | 5 | Есть targeted Avalonia.Headless UI test для exact/fuzzy/no-rebuild и full validation commands |
| 6. Готовность к автономной реализации | 5 | Нет открытых вопросов, scope small |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-02-roadmap-search-no-rebuild.md`; central stack `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`; локальный `AGENTS.override.md`; planned files `MainWindowViewModel.cs`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs`; current evidence from `MainWindowViewModel` search/emoji filters, `GraphControl` rebuild/highlight subscriptions, existing `RoadmapGraphUiTests`; open questions none.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: просмотрены текущие subscriptions в `MainWindowViewModel`, rebuild/highlight path в `GraphControl`, existing UI tests and counters.
  - Contract pass: spec сохраняет Non-Goals, не меняет public API, включает exact/fuzzy UI test coverage, stabilization requirement and validation commands.
  - Adversarial risk pass: counterexamples checked: late rebuild after debounce, clear-search rebuild, fuzzy-mode stale highlight, accidental exact-match fuzzy assertion, accidental topology change; mitigations are explicit in test design and scope.
  - Re-review after fixes / Fix and re-review: iteration 1 found MEDIUM test-design/scope gaps; iteration 2 left fuzzy as LOW follow-up; user widened scope; iteration 3 patched fuzzy into the main contract and re-reviewed changed sections.
  - Stop decision: PASS, после исправлений остались только LOW evidence residuals.
- Evidence inspected: `GraphControl` currently subscribes only to `Search.SearchText`, while `UpdateHighlights()` reads `Search.IsFuzzySearch`; `ActivateRoadmapProjection()` uses `emojiRootFilter`; existing test `RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode()` validates highlight state; existing helpers `WaitForStableRoadmapUpdates()` and `WaitForRoadmapUpdateAsync()` support stronger negative assertions.
- Depth checklist:
  - Scope drift / unrelated changes: только spec на SPEC phase.
  - Acceptance criteria: покрывают exact highlight, fuzzy toggle refresh, selection, topology stability, stable initial counters and no rebuild after set/toggle/clear.
  - Validation evidence: planned targeted UI test, build, full test.
  - Unsupported claims: no unsupported runtime claim; implementation not started.
  - Regression / edge case: clear search, late rebuild window, fuzzy-only typo query and active emoji filter cases учтены.
  - Comments/docs/changelog: не требуется.
  - Hidden contract change: non-roadmap search behavior explicitly preserved.
  - Manual-review challenge: reviewer would likely check whether root cause is ViewModel filter, not GraphControl; spec addresses that and requires delayed no-rebuild proof.
- No-findings justification: no BLOCKER/HIGH/MEDIUM findings remain after iteration 3.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video artifacts are not planned for headless unit UI path | Use explicit fallback evidence with test command output | accepted-risk |

- Fixed before continuing: MEDIUM test-design gap fixed by adding stabilization and negative wait requirements; fuzzy refresh moved from LOW follow-up into main scope and test contract.
- Checks rerun: SPEC linter/rubric self-check and third review pass over changed sections.
- Needs human: phrase `Спеку подтверждаю`.
- Residual risks / follow-ups: full `dotnet test src/Unlimotion.sln` may be slow or environment-sensitive; report exact blocker if it cannot run.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: `MainWindowViewModel` roadmap projection, `GraphControl` search highlight subscription, `RoadmapGraphUiTests` regression.
- Decision: можно завершать; validation limitation по full solution build зафиксирован как accepted-risk с объективной причиной и next-best evidence.
- Review passes:
  - Scope/Evidence pass: Изменения ограничены тремя planned code/test files и spec; ordinary tree `emojiRootFilter` не изменен.
  - Contract pass: Roadmap `Graph.Tasks` теперь использует search-independent root filter; `SearchText` и `IsFuzzySearch` вызывают только `UpdateHighlights()`.
  - Adversarial risk pass: Проверены exact search, fuzzy-only query, clear search, selected node/reference stability and no rebuild counters.
  - Re-review after fixes / Fix and re-review: Дополнительных code fixes после validation не потребовалось; spec review metadata и Approval section исправлены и повторно проверены.
  - Stop decision: Stop after targeted UI test + project build + diff check; full solution build ограничен окружением.
- Evidence inspected: diff измененных файлов; `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore`; `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode"`; `git diff --check`; failed/timeout full solution build attempts.
- Depth checklist:
  - Scope drift / unrelated changes: Нет.
  - Acceptance criteria: Покрыты no-rebuild для exact search, fuzzy toggle и clear; highlight меняется по текущему exact/fuzzy режиму.
  - Validation evidence: Targeted UI test passed 1/1; test project build passed; diff check passed.
  - Unsupported claims: Full solution build не заявляется как passed.
  - Regression / edge case: Covered negative wait windows after search text changes and fuzzy mode toggle.
  - Comments/docs/changelog: Production comments не добавлялись; spec journal обновлен.
  - Hidden contract change: Ordinary task tree search/filter path сохранен на старом `emojiRootFilter`.
  - Manual-review challenge: Основной остаточный риск - full solution build path по Android/mobile projects не завершен в текущем окружении.
- No-findings justification: Нет BLOCKER/HIGH/MEDIUM findings после review; оставшиеся риски validation-only и environment-only.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full solution build did not complete in current environment: `--no-restore` failed on missing `project.assets.json`, restore/build attempt timed out after 10 minutes while spawning Android/mobile build paths | Report limitation; rely on narrower passed build/test for this scoped change | accepted-risk |
| LOW | evidence | No video artifact for Avalonia.Headless UI path | Use command output and rebuild counters as fallback evidence | accepted-risk |

- Fixed before final report: Added roadmap-only root filter, fuzzy highlight subscription, regression assertions; corrected post-EXEC status/decision format and stale Approval section.
- Checks rerun: `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore`; targeted `dotnet test` with TUnit treenode filter; `git diff --check`.
- Validation evidence: test project build passed with existing warnings; targeted UI regression passed 1/1; diff check returned 0.
- Unrelated changes: Нет.
- Needs human: Нет.
- Residual risks / follow-ups: Full solution build may need a restored/mobile-ready environment; no video artifact for headless UI path.

## Approval
Получено подтверждение пользователя: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Context gathering | 0.85 | Нет | Создать рабочий spec | Нет | Нет | Найдены центральные правила, UI testing override и вероятный root cause | `AGENTS.override.md`, central instructions, `MainWindowViewModel.cs`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
| SPEC | Spec authoring | 0.90 | Подтверждение пользователя | Запросить approval gate | Да | Да, ожидается фраза `Спеку подтверждаю` | QUEST mode запрещает менять код до подтверждения | `specs/2026-06-02-roadmap-search-no-rebuild.md` |
| SPEC | Iterative spec review | 0.92 | Нет для SPEC review; EXEC approval still needed | Запросить approval gate | Да | Да, ожидается фраза `Спеку подтверждаю` | Итерация 1 нашла MEDIUM gaps в test design/scope; после patch итерация 2 оставила только LOW findings | `specs/2026-06-02-roadmap-search-no-rebuild.md` |
| SPEC | Scope update for fuzzy search | 0.93 | Нет для SPEC; EXEC approval still needed | Запросить approval gate | Да | Да, пользователь попросил включить `Search.IsFuzzySearch`; ожидается `Спеку подтверждаю` для EXEC | `Search.IsFuzzySearch` теперь входит в основной контракт: refresh highlight без rebuild; review iteration 3 оставила только LOW evidence finding | `specs/2026-06-02-roadmap-search-no-rebuild.md` |
| EXEC | Implementation | 0.90 | Нет | Запустить targeted validation | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | Добавлен `roadmapRootFilter` без зависимости от search, `GraphControl` подписан на `SearchText` + `IsFuzzySearch`, regression расширен no-rebuild assertions | `MainWindowViewModel.cs`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Validation | 0.86 | Full solution build не завершился: `--no-restore` missing assets, restore/build timeout на solution/mobile path | Выполнить post-EXEC review | Нет | Нет | Relevant UI regression passed 1/1, test project build passed, diff check passed; фоновые build/test хвосты остановлены по конкретным PID | build/test command output, process cleanup checks |
| EXEC | Post-EXEC review | 0.88 | Нет блокирующих данных | Финальный отчет пользователю | Нет | Нет | Review не выявил blocker/high/medium findings; residual risks ограничены full solution build environment и отсутствием video artifact для headless UI | измененные файлы и validation evidence |
| EXEC | Review format audit | 0.95 | Нет | Финальный отчет пользователю | Нет | Нет | Статус и decision post-EXEC review приведены к каноническим значениям `PASS` и `можно завершать`; code diff не менялся | `specs/2026-06-02-roadmap-search-no-rebuild.md` |
| EXEC | Approval section audit | 0.95 | Нет | Финальный отчет пользователю | Нет | Нет | Убрана stale-формулировка, будто подтверждение спеки еще ожидается; re-review section обновлена после spec-only fix | `specs/2026-06-02-roadmap-search-no-rebuild.md` |
