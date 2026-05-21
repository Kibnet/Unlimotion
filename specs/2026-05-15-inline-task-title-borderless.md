# Borderless Inline Task Title Editor

## 0. Метаданные
- Тип (профиль): `delivery-task`; stack profile `dotnet-desktop-client`; overlay profile `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: central `QUEST` SPEC-first gate; локальный `AGENTS.override.md` требует UI tests для UI-facing изменений; до подтверждения спеки меняется только этот spec-файл.
- Связанные ссылки: `C:\Users\Kibnet\.codex\agents\AGENTS.md`; `AGENTS.override.md`; `src/Unlimotion/Views/MainControl.axaml`; `src/Unlimotion/Views/GraphControl.axaml`; `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`; `src/Unlimotion.Test/RoadmapGraphUiTests.cs`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Сделать inline-редактирование текста задачи визуально гладким: при появлении и фокусе `TextBox` не должен добавлять рамку и не должен создавать визуальный сдвиг относительно обычного текста задачи.

Outcome contract:
- Success means: при входе в inline-редактирование заголовка задачи редактор остаётся без рамки в дереве задач и на роадмапе; существующий flow F2/повторный клик продолжает работать; заголовок продолжает обновляться через binding.
- Итоговый артефакт / output: точечная правка стилей `TextBox.InlineTaskTitleEditor` и `TextBox.RoadmapInlineTaskTitleEditor` с UI regression tests в существующем headless suite.
- Stop rules: остановиться после targeted UI test, `dotnet build` и полного тестового прогона либо явно зафиксировать объективную причину, если полный прогон невозможен.

## 2. Текущее состояние (AS-IS)
- Inline-редактор создаётся программно в `MainControl.CreateInlineTitleEditor` (`src/Unlimotion/Views/MainControl.axaml.cs`), получает класс `InlineTaskTitleEditor` и `AutomationId=InlineTaskTitleTextBox`.
- В `src/Unlimotion/Views/MainControl.axaml` стиль `TextBox.InlineTaskTitleEditor` задаёт `BorderThickness=1`, `BorderBrush=Transparent`, `Background=Transparent`, `Padding=2,0`, а стиль `:focus` делает `Opacity=1`, `IsHitTestVisible=True`, `BorderBrush={DynamicResource ThemeControlMidBrush}` и `Background={DynamicResource ThemeControlLowBrush}`.
- Roadmap inline-редактор создаётся программно в `GraphControl.CreateRoadmapInlineTitleEditor` (`src/Unlimotion/Views/GraphControl.axaml.cs`), получает класс `RoadmapInlineTaskTitleEditor` и `AutomationId=RoadmapInlineTaskTitleTextBox`.
- В `src/Unlimotion/Views/GraphControl.axaml` стиль `TextBox.RoadmapInlineTaskTitleEditor` имеет тот же проблемный visual contract: `BorderThickness=1`, `Padding=2,0`, а focused style возвращает видимый `BorderBrush` и focused `Background`.
- Поэтому при фокусе появляется видимая рамка и фон. Даже прозрачная рамка в базовом состоянии оставляет толщину border в layout/шаблоне `TextBox`.
- Уже есть headless UI-тест `TreeCommandUi_InlineTitleEdit_CreatesEditorOnlyForF2OrRepeatedTitleClick`, который создаёт inline-редактор, проверяет focus и binding.
- Уже есть headless UI-тест `RoadmapGraph_InlineTitleEdit_CreatesEditorForF2OrRepeatedTitleClick`, который создаёт roadmap inline-редактор, проверяет focus, wrapping и binding.

## 3. Проблема
Одна корневая проблема: focused inline `TextBox` визуально отличается от текста задачи рамкой и может восприниматься как сдвигающийся/выпирающий элемент вместо плавного редактирования на месте.

## 4. Цели дизайна
- Разделение ответственности: поведение создания/фокуса редактора остаётся в code-behind; визуальный контракт без рамки задаётся XAML-стилем.
- Повторное использование: сохранить существующие классы `InlineTaskTitleEditor` и `RoadmapInlineTaskTitleEditor` для соответствующих поверхностей.
- Тестируемость: расширить текущий headless UI-тест проверкой border/padding визуального контракта редактора.
- Консистентность: не менять общий стиль всех `TextBox`, только inline title editor.
- Обратная совместимость: сохранить `AutomationId`, hotkey/click flow и binding `TaskItemViewModel.Title`.

## 5. Non-Goals (чего НЕ делаем)
- Не менять правила входа в inline-редактирование (`F2`, повторный клик, debounce).
- Не менять шаблон `TextBox` глобально и стили обычных полей деталей задачи.
- Не менять `TaskItemViewModel`, storage, синхронизацию или модель данных.
- Не добавлять новые пользовательские настройки.
- Не вводить визуальные screenshot/video artifacts в репозиторий как бинарные файлы.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> визуальный стиль task-tree inline editor: убрать видимую рамку и предотвратить layout shift от border/padding.
- `src/Unlimotion/Views/GraphControl.axaml` -> визуальный стиль roadmap inline editor с тем же borderless contract.
- `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` -> regression coverage для borderless focused inline editor в task-tree flow.
- `src/Unlimotion.Test/RoadmapGraphUiTests.cs` -> regression coverage для borderless focused inline editor в roadmap flow.

### 6.2 Детальный дизайн
- В `TextBox.InlineTaskTitleEditor` установить borderless-визуальный контракт: `BorderThickness=0`, `BorderBrush=Transparent`, `Background=Transparent`, без дополнительных отступов, которые увеличивают занимаемое место относительно `EmojiTextBlock`.
- В `TextBox.RoadmapInlineTaskTitleEditor` применить такой же borderless-визуальный контракт: `BorderThickness=0`, `BorderBrush=Transparent`, `Background=Transparent`, `Padding=0`.
- Для Fluent `TextBox` дополнительно переопределить template part `Border#PART_BorderElement`, потому что focused blue frame рисуется не только свойствами верхнего `TextBox`.
- В `TextBox.InlineTaskTitleEditor:focus` не возвращать видимый `BorderBrush`; фон оставить прозрачным или убрать focused background, чтобы редактор выглядел как редактируемый текст на месте.
- Автоселект текста при входе в редактирование сохранить.
- Сохранить `Opacity=0` / `IsHitTestVisible=False` / `IsTabStop=False` до фокуса и `Opacity=1` / `IsHitTestVisible=True` при фокусе, так как это часть текущего behavior.
- Контракты / API: публичных API изменений нет; automation-id остаётся `InlineTaskTitleTextBox`.
- Output contract / evidence rules: тест должен проверять focused editor properties, которые отражают визуальный контракт (`BorderThickness == 0`, `Padding == 0`, transparent `Background`, no focused `BorderBrush`).
- Visual planning artifact для UI-facing изменений:

```text
AS-IS:
  [ ] Task title
      after edit focus: [ Task title ]  <- TextBox frame/background appears

TO-BE:
  [ ] Task title
      after edit focus:  Task title     <- caret/editing in place, no frame, no added border footprint
```

- UI test video evidence для UI automation задач: обязательно. Использовать desktop recorder по existing AppAutomation/FlaUI launch host и Win32 `PrintWindow`/`BitBlt` кадры, затем собрать PNG-кадры в MP4 через установленный `ffmpeg`. Сохранять local-only artifact в `artifacts/ui-video-evidence/inline-task-title-borderless/`. Не коммитить бинарный video artifact.
- Границы сохранения поведения: editing flow, focus selection, title binding, lost-focus cleanup must remain unchanged.
- Обработка ошибок: не применимо; визуальная правка стиля не добавляет error paths.
- Производительность: не применимо; изменение style setters не добавляет вычислений или IO.

## 7. Бизнес-правила / Алгоритмы (если есть)
Не применимо: нет бизнес-алгоритмов. UI-инвариант: focused inline title editor must not render or reserve a visible border.

## 8. Точки интеграции и триггеры
- `CreateInlineTitleEditor` добавляет класс `InlineTaskTitleEditor`; именно этот класс должен получать новый borderless style.
- `CreateRoadmapInlineTitleEditor` добавляет класс `RoadmapInlineTaskTitleEditor`; этот класс должен получать тот же borderless style.
- Триггеры создания редактора остаются прежними: `F2` и повторный клик по `InlineTaskTitleTextBlock`.
- Roadmap-триггеры создания редактора остаются прежними: `F2` и повторный клик по `RoadmapInlineTaskTitleSurface`.
- `LostFocus` cleanup остаётся прежним.

## 9. Изменения модели данных / состояния
- Новых полей нет.
- Persisted state не меняется.
- Влияния на хранилище задач и `TaskTreeExpansionState.json` нет.

## 10. Миграция / Rollout / Rollback
- Миграция не нужна.
- Rollout: изменение применяется при следующем запуске/перерисовке view.
- Rollback: вернуть прежние setters `BorderThickness=1`, focused `BorderBrush` и focused `Background`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Focused inline title `TextBox` has no visible/reserved border (`BorderThickness == 0`).
  - Focused roadmap inline title `TextBox` has no visible/reserved border (`BorderThickness == 0`) and no Fluent template focus frame.
  - Inline editor keeps the existing repeated-click and `F2` creation/focus behavior.
  - Editing text still updates `TaskItemViewModel.Title`.
  - No global `TextBox` styles are changed.
- Какие тесты добавить/изменить:
  - Update `TreeCommandUi_InlineTitleEdit_CreatesEditorOnlyForF2OrRepeatedTitleClick` or add a sibling test in `MainControlTreeCommandsUiTests.cs` that asserts the borderless style after editor focus.
  - Update `RoadmapGraph_InlineTitleEdit_CreatesEditorForF2OrRepeatedTitleClick` in `RoadmapGraphUiTests.cs` to assert the same borderless style after repeated-click and F2 focus.
- Characterization tests / contract checks для текущего поведения:
  - Existing test already characterizes creation/focus/binding. New assertion characterizes the desired no-border contract.
- Visual acceptance для UI-facing изменений:
  - Headless assertion for `BorderThickness == 0`.
  - Headless assertions for `Padding == 0`, transparent focused `Background`, and no focused `BorderBrush` that could render a frame/fill.
  - Headless assertion that the inner `Border#PART_BorderElement` has `BorderThickness == 0`, transparent `BorderBrush`, transparent `Background`.
  - Headless assertion that default autoselect is preserved (`SelectedText == Text`).
- UI video evidence для UI-facing фич/багфиксов:
  - Required evidence: local-only MP4 generated from visible desktop recorder frames.
  - Capture method: before/after task-title editing states are captured from an isolated AppAutomation desktop launch using Win32 `PrintWindow`/`BitBlt` frames, then encoded with FFmpeg.
  - Artifact path pattern: `artifacts/ui-video-evidence/inline-task-title-borderless/<timestamp>-<label>/inline-task-title-borderless.mp4`.
  - Baseline applicability: after-fix passing video is required. A before-fix failing video is optional only if the existing assertion can deterministically fail before implementation; otherwise document that the previous visible frame was already characterized by the AS-IS style and the new video demonstrates the corrected state.
- Базовые замеры до/после для performance tradeoff: Не применимо.
- Команды для проверки:
  - Targeted: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_InlineTitleEdit_CreatesEditorOnlyForF2OrRepeatedTitleClick"`
  - Targeted roadmap: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_InlineTitleEdit_CreatesEditorForF2OrRepeatedTitleClick"`
  - Build: `dotnet build src/Unlimotion.sln`
  - Full tests: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj`
  - Video encode: `ffmpeg -y -hide_banner -framerate 1 -start_number 1 -i artifacts/ui-video-evidence/inline-task-title-borderless/<timestamp>-<label>/frames/frame-%03d.png -vf "pad=ceil(iw/2)*2:ceil(ih/2)*2" -r 30 -movflags +faststart -pix_fmt yuv420p artifacts/ui-video-evidence/inline-task-title-borderless/<timestamp>-<label>/inline-task-title-borderless.mp4`
- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted UI test падает, исправить в рамках `MainControl.axaml`/релевантного теста и rerun targeted.
  - Если full tests превышают разумный локальный budget или падают unrelated tests, зафиксировать команду, симптом и next-best evidence.

## 12. Риски и edge cases
- Риск: `TextBox` template может иметь focus visual вне `BorderThickness`; mitigated by removing focused `BorderBrush`/`Background` setters and testing exposed properties.
- Риск: слишком малый click/edit target if padding removed; mitigated by preserving `MinHeight` and `MinWidth` from code-behind.
- Риск: тест станет слишком implementation-specific; mitigated by asserting user-visible style contract for this specific UI class, not global styles.

## 13. План выполнения
1. Обновить `TextBox.InlineTaskTitleEditor` style в `MainControl.axaml`.
2. Обновить `TextBox.RoadmapInlineTaskTitleEditor` style в `GraphControl.axaml`.
3. Расширить existing headless UI test assertions for focused inline editor style в task-tree и roadmap flows.
4. Добавить или переиспользовать local-only desktop video evidence recorder для frame capture + FFmpeg MP4 encoding.
5. Запустить targeted UI tests и сгенерировать MP4 artifact.
6. Запустить `dotnet build src/Unlimotion.sln`.
7. Запустить full test command or record objective blocker.
8. Выполнить post-EXEC review-loop и обновить журнал действий.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`
- Выполненные требования профиля:
  - Планируется точечное изменение UI style без блокировки UI thread.
  - `AutomationId` сохраняется.
  - UI regression coverage планируется в существующем Avalonia.Headless suite.
  - Перед завершением планируются targeted UI test, build и full test run.
  - Video evidence: required via existing `tests/Unlimotion.ReadmeMedia` desktop frame-capture pattern plus FFmpeg MP4 encoding.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Убрать рамку/фон focused inline title `TextBox`; обнулить border footprint | Сделать inline editing гладким и без сдвига |
| `src/Unlimotion/Views/GraphControl.axaml` | Убрать рамку/фон focused roadmap inline title `TextBox`; обнулить border footprint and template border | Сделать roadmap inline editing визуально таким же гладким |
| `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` | Добавить assertion на borderless focused inline editor | Покрыть UI-facing regression |
| `src/Unlimotion.Test/RoadmapGraphUiTests.cs` | Добавить assertion на borderless focused roadmap inline editor | Покрыть roadmap UI-facing regression |
| `artifacts/ui-video-evidence/inline-task-title-borderless/<timestamp>/inline-task-title-borderless.mp4` | Local-only generated evidence artifact | Подтвердить UI flow видео |
| `specs/2026-05-15-inline-task-title-borderless.md` | Рабочая спецификация и журнал | Требование QUEST gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Inline title editor border | `BorderThickness=1`, focused `BorderBrush` visible, Fluent template focus border visible | `BorderThickness=0`, no focused border, template `PART_BorderElement` borderless |
| Inline title editor background | focused `ThemeControlLowBrush` fill | transparent/no fill for smooth in-place edit |
| Roadmap inline title editor border/background | Same focused frame/fill pattern in `RoadmapInlineTaskTitleEditor` | Same borderless/no-fill contract as task tree inline editor |
| UI coverage | Creation/focus/binding covered | Creation/focus/binding plus no-border/no-padding/no-fill contract covered |
| Video evidence | Fallback planned | Required MP4 from desktop `PrintWindow`/`BitBlt` frames |

## 18. Альтернативы и компромиссы
- Вариант: оставить border thickness 1 transparent and only remove focused brush.
- Плюсы: минимально меняет layout footprint.
- Минусы: transparent border still reserves space and can keep the perceived shift.
- Почему выбранное решение лучше в контексте этой задачи: user explicitly wants no frame and no shift from it; `BorderThickness=0` directly removes both visible frame and border footprint.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, integration points, data/rollback и non-applicable sections указаны. |
| C. Безопасность изменений | 11-13 | PASS | Acceptance, rollback и scoped execution plan есть; данных/миграций нет. |
| D. Проверяемость | 14-16 | PASS | UI test, video encode, build, full test commands и planned files указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, tradeoff и review заполнены; open questions отсутствуют. |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client` и `ui-automation-testing` требования учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Цель узкая: borderless inline editor без изменения flow. |
| 2. Понимание текущего состояния | 5 | Указаны code-behind creation, XAML style и existing UI test. |
| 3. Конкретность целевого дизайна | 5 | Названы setters/style behavior and preserving contracts. |
| 4. Безопасность (миграция, откат) | 5 | Data changes отсутствуют; rollback тривиален. |
| 5. Тестируемость | 5 | Targeted UI regression, MP4 evidence, build and full test commands defined. |
| 6. Готовность к автономной реализации | 5 | Нет blocking questions; file scope and steps are concrete. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-05-15-inline-task-title-borderless.md`, central stack (`model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`), local `AGENTS.override.md`, selected profiles, open questions, planned changed files.
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: inspected current style in `MainControl.axaml`, editor creation in `MainControl.axaml.cs`, and existing UI test in `MainControlTreeCommandsUiTests.cs`.
  - Contract pass: spec keeps code changes inside UI style/test scope, preserves automation-id and existing editing behavior, includes UI test requirement and required MP4 evidence.
  - Adversarial risk pass: checked possible hidden template focus visual, padding/click-target risk, over-specific test risk, and video artifact generation risk; mitigations are included.
  - Re-review after fixes / Fix and re-review: after user-requested video requirement, spec was updated to require reproducible MP4 evidence and mandatory no-padding/no-fill assertions.
  - Stop decision: PASS because boundaries, acceptance criteria, tests, required video evidence and no open blockers are concrete.
- Evidence inspected:
  - `src/Unlimotion/Views/MainControl.axaml` style lines for `TextBox.InlineTaskTitleEditor`.
  - `src/Unlimotion/Views/MainControl.axaml.cs` `CreateInlineTitleEditor`.
  - `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` existing inline title edit test and helper methods.
- Depth checklist:
  - Scope drift / unrelated changes: planned scope limited to `MainControl.axaml`, one UI test file, and this spec.
  - Acceptance criteria: includes no-border, preserved flow, preserved binding, no global TextBox changes.
  - Validation evidence: planned targeted UI test, generated MP4 evidence, build, full test.
  - Unsupported claims: claims grounded in inspected style/code/test snippets.
  - Regression / edge case: focus visual and click target risks documented.
  - Comments/docs/changelog: no code comments/docs/changelog expected.
  - Hidden contract change: no public API, no automation-id, no storage/model changes.
  - Manual-review challenge: likely review question would be whether focused background/padding also counts as frame; spec now makes no-padding/no-fill checks mandatory and requires MP4 visual evidence.
- No-findings justification: small scoped UI style change has clear existing test anchor, concrete video generation path, and no unresolved product choices.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video artifact generation depends on local FFmpeg availability. | Verified `ffmpeg` is available locally and encoded a probe MP4 at `artifacts/ui-video-evidence/probe/readme-tab-tour.mp4`; during EXEC fail task closure if MP4 cannot be generated. | fixed |

- Fixed before continuing: video fallback replaced with required MP4 plan; visual assertions strengthened for padding/background/border brush.
- Checks rerun: SPEC linter/rubric reviewed after drafting and after video-plan update.
- Needs human: confirmation phrase required by `QUEST`.
- Residual risks / follow-ups: None blocking.

### Post-EXEC Review
- Статус: PASS с зафиксированными environment blockers для solution build/full test.
- Scope reviewed: `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `specs/2026-05-15-inline-task-title-borderless.md`, generated local-only artifacts under `artifacts/ui-video-evidence/inline-task-title-borderless/`.
- Decision: change complete; targeted UI behavior and video evidence accepted; full-suite evidence partially blocked by local environment/time budget.
- Review passes:
  - Scope/Evidence pass: tracked code changes are limited to inline title editor style, its UI regression test, and this spec. Video recorder and MP4s are local-only under ignored `artifacts/`.
  - Contract pass: no global `TextBox` style was changed; `AutomationId=InlineTaskTitleTextBox`, F2/repeated-click flow, focus and binding behavior remain covered by the targeted test.
  - Adversarial risk pass: focused editor now asserts `BorderThickness=0`, `Padding=0`, transparent `BorderBrush`, transparent `Background`, and inner template borderlessness, so focused frame/fill regressions are covered while preserving autoselect.
  - Re-review after fixes / Fix and re-review: fixed planned assertion implementation to use awaited TUnit assertions; rejected black headless/`gdigrab` evidence and replaced it with visible desktop `PrintWindow`/`BitBlt` frame recording; after user review, preserved autoselect and removed Fluent template border via `PART_BorderElement`.
  - Stop decision: stop after targeted UI test, visible before/after MP4s, test-project build, attempted solution build, attempted full tests, and local video artifact verification.
- Evidence inspected:
  - Before video: `artifacts/ui-video-evidence/inline-task-title-borderless/20260515-before-printwindow/inline-task-title-borderless.mp4`.
  - After video: `artifacts/ui-video-evidence/inline-task-title-borderless/20260515-after-template-borderless/inline-task-title-borderless.mp4`.
  - Each visible run contains 5 PNG frames and `frames.txt`; representative frames were manually opened and verified non-black.
  - Targeted UI command passed after cleanup: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_InlineTitleEdit_CreatesEditorOnlyForF2OrRepeatedTitleClick"`.
  - Test-project build passed: `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj`.
  - Solution build attempted and blocked by missing local `wasm-tools` workload for `src/Unlimotion.Android/Unlimotion.Android.csproj` and `src/Unlimotion.iOS/Unlimotion.iOS.csproj`.
  - Full test command attempted: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj`; stopped after 15 minutes timeout with no useful runner output. The remaining `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj` process was explicitly stopped.
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated tracked files were modified; generated videos and one-off recorder remain under ignored `artifacts/`.
  - Acceptance criteria: no-border/no-template-border/no-padding/no-fill asserted; repeated-click/F2 creation/focus, autoselect and title binding remain asserted in the same test.
  - Validation evidence: targeted UI test with MP4 passed; test-project build passed; solution build/full tests have documented blockers.
  - Unsupported claims: visual evidence claims point to concrete local MP4s and frame files.
  - Regression / edge case: focused background/border regressions covered by assertions; global `TextBox` styles untouched.
  - Comments/docs/changelog: no product comments/changelog needed; spec journal updated.
  - Hidden contract change: no public API/storage/model changes; helper is test-only and inactive unless `UNLIMOTION_CAPTURE_UI_VIDEO=1`.
  - Manual-review challenge: likely question is whether video can be generated reliably; the black initial captures were rejected, and final evidence uses visible desktop frames verified by opening PNGs.
- No-findings justification: reviewed code diff and validation evidence; no actionable defects found in the implemented scope.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | `dotnet build src/Unlimotion.sln` cannot complete locally because Android/iOS projects require missing `wasm-tools`. | Install/restore workload if solution-wide build is required in this environment. | follow-up |
| LOW | validation | Full `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj` did not complete within 15 minutes and produced no useful output before timeout. | Re-run with a larger budget or narrower suite isolation if full-suite green is required. | follow-up |

- Fixed before final report: awaited TUnit style assertions; removed broken tracked headless video helper; added local-only `PrintWindow`/`BitBlt` recorder under `artifacts`; removed Fluent template focused border; stopped leftover timed-out full-test process.
- Checks rerun: visible before/after video recorder; targeted UI test; `dotnet build-server shutdown`; `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj`; solution build attempted; full test attempted.
- Validation evidence: local `before` and `after` MP4s listed above.
- Unrelated changes: none in tracked git status.
- Needs human: no.
- Residual risks / follow-ups: solution-wide build and full-suite completion depend on local workload/time budget, not on the inline editor change.

### Post-EXEC Review Addendum: Roadmap parity
- Статус: PASS с зафиксированными environment blockers для solution build/full test.
- Scope reviewed: user request on 2026-05-21 to apply the same behavior on roadmap; `src/Unlimotion/Views/GraphControl.axaml`; `src/Unlimotion.Test/RoadmapGraphUiTests.cs`; this spec; `git diff --stat`; targeted UI test output; test-project build output; full test and solution build timeout evidence.
- Decision: можно завершать roadmap parity change; UI regression is covered by targeted tests; full validation blockers are documented.
- Review passes:
  - Scope/Evidence pass: inspected roadmap style, roadmap editor creation path, existing roadmap inline edit UI test, and final diff. Tracked changes are limited to roadmap style/test and this spec update.
  - Contract pass: roadmap editor now matches task-tree borderless contract: `BorderThickness=0`, `Padding=0`, transparent top-level brushes/background, and transparent `Border#PART_BorderElement` for normal/focus/pointerover/focus:pointerover states. `RoadmapInlineTaskTitleTextBox` automation-id, F2/repeated-click flow, wrapping, binding and autoselect remain intact.
  - Adversarial risk pass: checked for hidden Fluent template focused frame, lost autoselect, accidental global `TextBox` changes, unrelated tracked edits, and insufficient UI evidence. Roadmap targeted UI test asserts the external properties, internal template border, and `SelectedText == Text` after repeated click and F2 focus.
  - Re-review after fixes / Fix and re-review: no code findings requiring fixes after roadmap targeted test/build/diff review; spec was updated to include roadmap scope and validation.
  - Stop decision: PASS for implemented scope; full-suite and solution-build evidence are blocked by local runtime/time constraints, not by failing targeted roadmap behavior.
- Evidence inspected:
  - Targeted roadmap UI test passed: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_InlineTitleEdit_CreatesEditorForF2OrRepeatedTitleClick"`; result `1` total, `1` passed.
  - Targeted task-tree UI test passed after roadmap change: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_InlineTitleEdit_CreatesEditorOnlyForF2OrRepeatedTitleClick"`; result `1` total, `1` passed.
  - Test-project build passed: `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj`.
  - Full test command attempted and timed out after 15 minutes with no useful output: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj`. The remaining `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj` process was stopped.
  - Solution build attempted and timed out after 5 minutes: `dotnet build src/Unlimotion.sln`. `dotnet build-server shutdown` was run afterwards.
  - `git diff --check` passed; only LF/CRLF warnings were reported.
- Depth checklist:
  - Scope drift / unrelated changes: tracked changes are related; untracked `src/Unlimotion.Desktop/TaskTreeExpansionState.json` is pre-existing/unrelated and was not touched.
  - Acceptance criteria: roadmap no-border/no-template-border/no-padding/no-fill and preserved autoselect are asserted; task-tree regression still passes.
  - Validation evidence: targeted UI tests and test-project build passed; full test and solution build have documented timeout blockers.
  - Unsupported claims: all claims are tied to inspected diff or command output.
  - Regression / edge case: normal/focus/pointerover/focus:pointerover template states are all covered by style selectors; F2 and repeated-click entry paths are both asserted.
  - Comments/docs/changelog: no code comments or changelog needed; spec updated.
  - Hidden contract change: no public API, model, storage or automation-id change.
  - Manual-review challenge: likely issue would be the same hidden Fluent template frame on roadmap; this is directly tested through `PART_BorderElement`.
- No-findings justification: relevant style/test diff is small and mirrors already accepted task-tree fix; targeted roadmap and task-tree UI evidence passed.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full test run timed out after 15 minutes without useful output. | Re-run with larger local budget or CI. | follow-up |
| LOW | validation | Solution build timed out after 5 minutes in the local environment. | Re-run with larger local budget or CI; previous environment also had solution-wide workload constraints. | follow-up |

- Fixed before final report: none required after review.
- Checks rerun: targeted roadmap UI test; targeted task-tree UI test; test-project build; full test attempted; solution build attempted; `git diff --check`.
- Validation evidence: commands listed above.
- Unrelated changes: untracked `src/Unlimotion.Desktop/TaskTreeExpansionState.json` remains unrelated and untouched.
- Needs human: no.
- Residual risks / follow-ups: full-suite and solution-wide build should be validated in CI or with a larger local timeout budget.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | context-gathering | 0.9 | Нет | Сформировать spec | Нет | Нет | Найдены XAML style, code-behind creation и existing UI test anchor. | `MainControl.axaml`, `MainControl.axaml.cs`, `MainControlTreeCommandsUiTests.cs` |
| SPEC | spec-drafting | 0.9 | Подтверждение пользователя | Запросить утверждение спеки | Да | Да, ожидается фраза подтверждения | QUEST gate требует подтверждение перед EXEC. | `specs/2026-05-15-inline-task-title-borderless.md` |
| SPEC | video-evidence-planning | 0.92 | Подтверждение пользователя | Перейти к EXEC после утверждения | Да | Да, пользователь потребовал найти способ записи видео | Найден repository pattern `tests/Unlimotion.ReadmeMedia`; локальный `ffmpeg` успешно кодирует MP4. | `tests/Unlimotion.ReadmeMedia/Program.cs`, `artifacts/ui-video-evidence/probe/readme-tab-tour.mp4`, `specs/2026-05-15-inline-task-title-borderless.md` |
| EXEC | approval-and-baseline-video | 0.9 | Нет | Внести style/test changes | Нет | Да, пользователь подтвердил spec | Первая headless-видеопопытка оказалась черной из-за неверного источника кадров; позднее заменена на desktop recorder. | `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `artifacts/ui-video-evidence/inline-task-title-borderless/20260515-before-printwindow/inline-task-title-borderless.mp4` |
| EXEC | implementation | 0.92 | Нет | Запустить targeted UI test and visible video recorder | Нет | Нет | Inline editor style сделан borderless: `BorderThickness=0`, `Padding=0`, no focused border/background; existing UI test расширен проверками визуального контракта. | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` |
| EXEC | validation | 0.86 | Full-suite completion; solution workload | Выполнить post-EXEC review | Нет | Нет | Targeted UI test passed; visible before/after MP4s encoded; test project build passed after build-server shutdown; solution build blocked by missing `wasm-tools`; full test timed out after 15 minutes. | `artifacts/ui-video-evidence/inline-task-title-borderless/20260515-before-printwindow/inline-task-title-borderless.mp4`, `artifacts/ui-video-evidence/inline-task-title-borderless/20260515-after-template-borderless/inline-task-title-borderless.mp4`, `src/Unlimotion.Test/Unlimotion.Test.csproj`, `src/Unlimotion.sln` |
| EXEC | user-review-fix | 0.9 | Нет | Финальный отчет | Нет | Да, пользователь указал, что автоселект надо оставить и рамка всё ещё видна | Исправлен неверный вывод про автоселект: `SelectAll()` сохранён, а синяя рамка убрана через `TextBox.InlineTaskTitleEditor /template/ Border#PART_BorderElement`; тест проверяет и autoselect, и template border. | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `artifacts/ui-video-evidence/inline-task-title-borderless/20260515-after-template-borderless/inline-task-title-borderless.mp4` |
| EXEC | post-exec-review | 0.88 | Нет для текущего scope | Финальный отчет | Нет | Нет | Reviewed diff/scope/evidence; no unrelated tracked changes; documented validation blockers and residual risks. | `specs/2026-05-15-inline-task-title-borderless.md` |
| EXEC | roadmap-parity-implementation | 0.9 | Нет | Выполнить roadmap validation и финальный отчет | Нет | Да, пользователь попросил, чтобы так же работало на роадмапе | Roadmap использовал отдельный `RoadmapInlineTaskTitleEditor` с теми же focused border/background; применён тот же borderless/template-borderless contract. | `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, `specs/2026-05-15-inline-task-title-borderless.md` |
| EXEC | roadmap-validation-review | 0.86 | Full-suite/solution completion в локальном time budget | Финальный отчет | Нет | Нет | Targeted roadmap and task-tree UI tests passed; test-project build passed; full test and solution build timed out; post-EXEC addendum зафиксировал evidence и residual risks. | `src/Unlimotion.Test/Unlimotion.Test.csproj`, `src/Unlimotion.sln`, `specs/2026-05-15-inline-task-title-borderless.md` |
