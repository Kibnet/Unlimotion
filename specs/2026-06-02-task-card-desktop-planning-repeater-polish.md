# Task Card Desktop Planning and Repeater Polish

## 0. Метаданные
- Тип (профиль): `delivery-task`; профили `dotnet-desktop-client`, `ui-automation-testing`; контексты `testing-dotnet`, `visual-feedback`.
- Владелец: Codex / пользователь.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая ветка `codex/task-card-redesign`.
- Ограничения: QUEST mode; до фразы `Спеку подтверждаю` менять только эту спецификацию; UI-facing изменение обязано обновить UI tests и пройти релевантные UI проверки; desktop normal width должен улучшиться без регресса phone/narrow layout.
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `specs/2026-05-15-task-card-mobile-ux-follow-up.md`, `specs/2026-05-15-task-card-visual-polish.md`, user-provided screenshot in current chat.

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Довести desktop-версию карточки задачи на нормальной ширине до аккуратного, компактного рабочего layout: поля `Планирование` и `Шаблоны повторения` должны стоять предсказуемыми строками/группами, без странных переносов, пустых зон и визуально тяжёлых широких weekday-кнопок. Narrow/phone layout, ради которого были добавлены compact rules, должен остаться рабочим.

Outcome contract:
- Success means: на desktop-width карточке планирование читается как компактная сетка `начало / длительность / окончание`, quick-action controls привязаны к соответствующим полям, repeater selector и pattern controls выглядят как одна собранная группа, weekday toggles не занимают лишнюю высоту и не выглядят как крупная отдельная панель; phone widths `360/390/430` не получают горизонтальный overflow.
- Итоговый артефакт / output: XAML/style/code-behind правки desktop layout, обновлённые Avalonia.Headless assertions, UX screenshot evidence или fallback evidence, обновлённый журнал спеки.
- Stop rules: остановиться после passing targeted task-card layout UI tests, релевантного headless smoke, screenshot evidence или объективного blocker, post-EXEC review и отчёта пользователю.

## 2. Текущее состояние (AS-IS)
- Основной UI живёт в `src/Unlimotion/Views/MainControl.axaml`.
- Адаптивные desktop/compact ширины применяются в `src/Unlimotion/Views/MainControl.axaml.cs`, метод `ApplyTaskDetailsMeasuredWidths`.
- UI layout coverage живёт в `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`.
- Selectors для automation/evidence живут в `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`.
- UX screenshot capture уже есть в `tests/Unlimotion.ReadmeMedia/Program.cs` через `--ux-review task-card`.
- По screenshot пользователя на desktop:
  - `Планирование` переносится как две колонки, где окончание уходит под начало, а длительность остаётся отдельной второй колонкой;
  - quick-action dropdowns занимают отдельные строки и визуально отрываются от полей;
  - repeater selector, type, period, after-complete и weekdays выглядят слишком крупно и растянуто;
  - weekday buttons занимают много вертикального пространства.
- Последний targeted run перед этой spec падал: `MainControlTaskCardLayoutUiTests` -> 2 failed / 2 passed. Падения: desktop visibility assertion и phone-width overflow на `430`.

## 3. Проблема
Компактные настройки, добавленные для phone/narrow layout, ухудшили desktop normal-width композицию: planning/repeater controls перестали выглядеть как плотная форма и начали переноситься/растягиваться не по смысловым группам.

## 4. Цели дизайна
- Разделение ответственности: XAML задаёт структуру semantic groups; code-behind только переключает measured widths для compact/regular modes.
- Повторное использование: сохранить существующие classes и automation ids.
- Тестируемость: добавить assertions на desktop placement для planning/repeater controls и оставить phone overflow coverage.
- Консистентность: desktop должен быть плотнее и спокойнее, но без отдельного desktop-only visual system.
- Обратная совместимость: bindings, commands, localization resources и automation ids не меняются.

## 5. Non-Goals (чего НЕ делаем)
- Не redesign всей карточки задачи.
- Не менять ViewModel, доменную модель повторений или planning semantics.
- Не менять тексты локализации, кроме объективно необходимой мелкой copy-правки, если она всплывёт.
- Не добавлять новые пользовательские действия.
- Не коммитить screenshot/video artifacts по умолчанию.
- Не чинить unrelated failing tests вне task-card layout scope.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> compact desktop structure for planning/repeater: grouped layout, margins, widths, weekday toggle sizing.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> regular/compact measured widths and class toggles without changing behavior.
- `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` -> desktop placement assertions and existing phone containment assertions.
- `tests/Unlimotion.ReadmeMedia/Program.cs` -> use existing screenshot capture command; no functional change expected unless selectors need small support.

### 6.2 Детальный дизайн
- Planning desktop target:
  - use three semantic groups: begin, duration, end;
  - at normal desktop width, all three groups fit in one row when the card has enough width;
  - each group contains value control and its quick action directly below or inside the same group, with consistent width and spacing;
  - group width should be enough for Russian labels without clipping, but not so wide that only two groups fit in the screenshot scenario.
- Repeater desktop target:
  - top selector stays a compact leading control;
  - type, period and `После выполнения` stay in the same row where width permits;
  - weekdays become smaller pills with predictable wrapping and less vertical weight.
- Compact/phone target:
  - current one-column intent remains;
  - controls remain fully contained at `360`, `390`, `430`;
  - overflow assertions should ignore harmless template internals only where they are false positives, not real user-facing overflow.
- Visual planning artifact:

```text
Desktop normal width target

Планирование
+----------------------+ +----------------------+ +----------------------+
| Планируемое начало   | | Планируемая длит.   | | Планируемое оконч.  |
| 15.01.2024           | |                      | | 15.01.2024          |
| Задать начало   v    | | Задать длительность | | Задать окончание v  |
+----------------------+ +----------------------+ +----------------------+

Шаблоны повторения
+------------------------------+  +---------------+ +-------+ [ ] После выполнения
| Еженедельно, каждые 1      v |  | Еженедельно v | | 1  ^v |
+------------------------------+  +---------------+ +-------+
[Пн] [Вт] [Ср] [Чт] [Пт] [Сб] [Вс]   small, even pills
```

- UI test video evidence:
  - During EXEC, first attempt video evidence if an available local recorder can safely capture the real app window launched by the automation flow.
  - If video capture is blocked, report the objective blocker: no repository UI harness video artifact support, recorder unavailable, window capture failure, unsafe capture, or policy/size constraint.
  - Fallback evidence when video is blocked: targeted Avalonia.Headless assertions plus `artifacts/ux-review/<stamp>-task-card/` screenshots/report, local-only unless пользователь попросит закоммитить.
  - Before/repro evidence for this bug is the user-provided screenshot plus the deterministic failing `MainControlTaskCardLayoutUiTests` run; an extra before video is required only if a stable automated recorder is available without delaying the fix.
- Границы сохранения поведения: commands/bindings/automation ids сохраняются; изменения касаются layout, styles and measured widths.
- Обработка ошибок: не применимо; нет новых error paths.
- Производительность: visual descendants width pass уже существует; изменения не должны добавлять extra subscriptions or repeated traversal loops.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Не применимо: бизнес-логика planning/repeater не меняется.
- Layout invariant: desktop normal-width planning should prefer one row of three groups before wrapping.
- Layout invariant: phone/narrow mode should prefer one-column groups and no horizontal overflow.

## 8. Точки интеграции и триггеры
- `AttachedToVisualTree`, `PropertyChanged` and bounds observation continue to call `QueueTaskDetailsLayoutUpdate`.
- `ApplyTaskDetailsMeasuredWidths` applies regular vs compact widths for planning/repeater controls.
- UI tests arrange `MainControl` at desktop and phone dimensions and assert visible/contained controls.

## 9. Изменения модели данных / состояния
- Новых persisted или calculated data fields нет.
- Возможны только UI class/width constants or style values.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется.
- Rollout: обычный desktop UI update in current branch.
- Rollback: revert this task's commit restores previous layout; no data compatibility impact.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Desktop `1400x900` task-card layout exposes all key controls and no longer fails existing desktop visibility assertion.
  - Desktop planning groups fit as a compact row at normal card width or wrap only at semantic boundaries.
  - Desktop repeater pattern controls stay visually grouped; weekday toggles are smaller and wrap predictably.
  - Phone widths `360`, `390`, `430` pass no-horizontal-overflow and relation editor usability assertions.
  - Existing automation ids used by tests and screenshot capture remain stable.
- Какие тесты добавить/изменить:
  - Update `MainControlTaskCardLayoutUiTests` with desktop planning/repeater placement checks.
  - Keep or refine phone overflow assertions to distinguish real user-facing overflow from template false positives.
- Characterization tests / contract checks: current failing targeted run is the repro evidence.
- Visual acceptance: compare generated desktop screenshot against the wireframe above and user-provided screenshot defects; screenshot should show compact planning/repeater blocks without the current awkward row breaks.
- UI video evidence: attempt a safe local window recording first; if blocked, use PNG/report fallback as described in section 6.2 and record the exact blocker.
- Базовые замеры performance: не применимо.
- Команды для проверки:

```powershell
dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --no-progress
dotnet run --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*" --no-progress
dotnet run --project .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review task-card --language ru --output-root artifacts\ux-review\<stamp>-task-card
dotnet build .\src\Unlimotion.sln --no-restore -m:1
dotnet test .\src\Unlimotion.sln -- --no-progress
git diff --check
```

- Stop rules для test/retrieval/tool/validation loops:
  - Stop targeted loop when task-card UI tests pass or reveal a blocker requiring a design change.
  - Stop full validation if known workload/environment blockers appear; report exact blocker and run next-best available tests.
  - Do not proceed to final without post-EXEC review and at least targeted UI evidence.

## 12. Риски и edge cases
- Russian labels are wider than English and can force wrapping. Mitigation: test/capture with `--language ru` and use widths that fit Russian labels.
- Avalonia template internals can look like overflow although user-facing control is contained. Mitigation: keep assertions focused on user-facing controls or explicitly filter template parts with justification.
- Making weekday pills too small can reduce readability. Mitigation: reduce from current oversized buttons, not below readable min height.
- Desktop improvements can regress phone layout. Mitigation: keep `360/390/430` assertions.

## 13. План выполнения
1. Add or adjust desktop layout assertions for planning and repeater placement.
2. Update XAML styles/structure and measured desktop widths while preserving compact mode.
3. Run targeted layout tests and fix deterministic failures.
4. Generate or attempt UX screenshot evidence for Russian desktop.
5. Run headless smoke, build/full tests where environment allows, `git diff --check`.
6. Perform post-EXEC review and report results.

## 14. Открытые вопросы
- Нет блокирующих вопросов. Product target inferred from user screenshot: desktop should be compact and elegant, especially planning/repeater.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля:
  - UI changes will preserve automation ids and update Avalonia.Headless tests.
  - Relevant UI tests will be run before completion.
  - Build/full test run will be attempted; blockers will be reported with next-best checks.
  - Visual evidence will attempt safe video capture first; if blocked, it will use the existing screenshot workflow as documented fallback.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Planning/repeater layout and style polish | Fix desktop composition |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Regular/compact measured widths if needed | Keep desktop and phone width behavior stable |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Desktop placement assertions, phone overflow stability | Regression coverage |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | Short weekday labels for compact repeater controls | Keep English weekday buttons readable without wide full labels |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | Short weekday labels for compact repeater controls | Keep Russian weekday buttons readable without wide full labels |
| `tests/Unlimotion.ReadmeMedia/Program.cs` | No expected change; selector support only if needed | Screenshot evidence compatibility |
| `specs/2026-06-02-task-card-desktop-planning-repeater-polish.md` | Journal and EXEC evidence | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Planning desktop | Awkward two-column wrap; end field under begin | Three compact semantic groups when normal width allows |
| Planning actions | Dropdowns look detached on separate rows | Each action visually belongs to its field group |
| Repeater pattern | Controls and weekdays feel oversized/heavy | Pattern row is compact; weekday pills are smaller |
| Phone layout | Intended compact one-column, currently has `430` test failure | No horizontal overflow at `360/390/430` |

## 18. Альтернативы и компромиссы
- Вариант: only adjust widths in code-behind.
- Плюсы: minimal code change.
- Минусы: may preserve awkward semantic grouping and only shift breakpoints.
- Почему выбранное решение лучше в контексте этой задачи: the defect is layout composition, not only numeric width; XAML grouping and measured widths need to agree.

- Вариант: make planning/repeater fully grid-based for all widths.
- Плюсы: strongest alignment.
- Минусы: higher risk for phone layout and larger XAML rewrite.
- Почему выбранное решение лучше в контексте этой задачи: scoped polish should keep existing responsive pattern and improve desktop without broad rewrite.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals and Non-Goals заполнены |
| B. Качество дизайна | 6-10 | PASS | Responsibility, integration, invariants, rollback and state impact specified |
| C. Безопасность изменений | 11-13 | PASS | No data migration; rollback and edge cases covered |
| D. Проверяемость | 14-16 | PASS | Acceptance, UI tests, commands, video attempt and visual fallback evidence listed |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan, no blocking questions, file table and alternatives present |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation requirements mapped |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope is desktop planning/repeater polish plus phone regression guard |
| 2. Понимание текущего состояния | 5 | Current files, screenshot defects and failing targeted tests captured |
| 3. Конкретность целевого дизайна | 5 | Wireframe and placement rules define the target |
| 4. Безопасность (миграция, откат) | 5 | No data changes; rollback is simple revert |
| 5. Тестируемость | 5 | Targeted UI assertions, screenshot evidence and broader checks specified |
| 6. Готовность к автономной реализации | 5 | No blocking questions; implementation plan and file list are bounded |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS after review fixes
- Scope reviewed: `specs/2026-06-02-task-card-desktop-planning-repeater-polish.md`, central `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `visual-feedback`, local `AGENTS.override.md`, `tests/Unlimotion.ReadmeMedia/Program.cs` capture option parsing, planned changed files in sections 6 and 16.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: inspected user screenshot, XAML planning/repeater selectors, task-card layout tests, prior targeted failure summary, and actual `CaptureOptions.Parse` keys.
  - Contract pass: spec stays inside task-card UI layout, preserves automation ids, includes required UI tests/evidence, and now uses the repository's real `--output-root` capture option.
  - Adversarial risk pass: checked risks for Russian labels, phone overflow, template false positives, overbroad rewrite, broken validation commands, and weak video fallback wording.
  - Re-review after fixes / Fix and re-review: fixed UX capture command and video/fallback contract; rechecked sections 6.2, 11 and review table.
  - Stop decision: PASS; only user confirmation is required to enter EXEC.
- Evidence inspected: `src/Unlimotion/Views/MainControl.axaml` planning/repeater locations, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `tests/Unlimotion.ReadmeMedia/Program.cs` `CaptureOptions.Parse`, screenshot in current chat, previous targeted test output.
- Depth checklist:
  - Scope drift / unrelated changes: bounded to task-card layout/test/spec.
  - Acceptance criteria: measurable with desktop/phone UI tests and screenshots.
  - Validation evidence: commands listed with actual `--output-root`; targeted repro known.
  - Unsupported claims: visual target marked as inferred from user screenshot.
  - Regression / edge case: phone widths and Russian labels covered.
  - Comments/docs/changelog: no comments/changelog planned.
  - Hidden contract change: automation ids and bindings must remain stable.
  - Manual-review challenge: likely manual review would focus on whether weekday pills remain readable, whether duration empty state still looks intentional, and whether video fallback is too weak; covered by screenshot evidence, acceptance, and an explicit video-attempt/fallback contract.
- No-findings justification: after fixing the command and evidence contract, remaining findings are LOW tradeoffs only; spec has clear scope, visual planning artifact, tests, fallback evidence and no unresolved product choice.

Closed findings above LOW:

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | validation | UX capture command used non-existent `--output` argument; actual parser supports `--output-root` | Replace command with `--output-root` and re-review validation section | fixed |
| MEDIUM | evidence | Video evidence fallback was asserted without requiring a recorder attempt or objective blocker | Require video attempt where safe/available and exact blocker before screenshot fallback | fixed |

Remaining findings after fixes:

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Even with the stronger contract, repository task-card evidence is primarily PNG/report based unless a local recorder works | Use screenshot fallback and report command/path/blocker | accepted-risk |
| LOW | visual acceptance | Wireframe still cannot fully prove subjective polish such as "elegant" without human review of screenshots | Generate screenshots and ask for human visual review if product density remains debatable | accepted-risk |

- Fixed before continuing: corrected `--output-root`; strengthened UI video evidence and fallback rules.
- Checks rerun: SPEC linter/rubric reviewed after fixes; affected sections 6.2, 11 and Post-SPEC Review rechecked.
- Needs human: фраза `Спеку подтверждаю`.
- Residual risks / follow-ups: full solution validation may be blocked by known workload/environment issues; video evidence may remain local-only or blocked by recorder/harness availability.

### Post-EXEC Review
- Статус: PASS with LOW residual blockers
- Scope reviewed: approved spec, `git status --short`, changed XAML/code-behind/test/resources/spec diff, fresh weekly screenshot evidence under `artifacts/ux-review/20260602-235713-task-card-weekly-confirmation/`, targeted task-card UI test output, headless smoke output, affected project builds, `git diff --check`, and repeated full solution build attempt.
- Decision: можно отдавать результат пользователю; user-reported weekly wrapping defect is now covered by an assertion and confirmed by a fresh screenshot.
- Review passes:
  - Scope/Evidence pass: inspected `MainControl.axaml`, `MainControl.axaml.cs`, `MainControlTaskCardLayoutUiTests.cs`, localized weekday resources, `desktop/repeater-planning.png`, `report.json`, and git status after reverting temporary ReadmeMedia capture change.
  - Contract pass: automation ids and bindings are preserved; planning remains compact; weekly repeater now renders seven short weekday toggles in one desktop row; phone widths `360/390/430` remain covered by targeted UI tests.
  - Adversarial risk pass: specifically challenged the user's latest counterexample where `Вс` wrapped; fresh weekly PNG and test assertion both cover that counterexample. Rechecked temporary capture mutation did not remain in status.
  - Re-review after fixes / Fix and re-review: narrowed weekday buttons to fixed short-label pills, reran targeted layout tests, regenerated weekly screenshot, visually inspected the result, reverted temporary capture task id, rebuilt ReadmeMedia, reran headless smoke and diff check.
  - Stop decision: PASS; remaining findings are validation/evidence limitations, not known UI defects.
- Evidence inspected: `artifacts/ux-review/20260602-235713-task-card-weekly-confirmation/desktop/repeater-planning.png`, `artifacts/ux-review/20260602-235713-task-card-weekly-confirmation/report.json` with `Warnings: []`, targeted task-card UI tests 5/5, headless smoke 8/8, affected project builds, `git diff --check`, full solution build timeout.
- Depth checklist:
  - Scope drift / unrelated changes: source changes remain bounded to task-card layout, localized weekday labels, regression tests and spec; temporary `tests/Unlimotion.ReadmeMedia/Program.cs` change was reverted and refreshed out of status.
  - Acceptance criteria: desktop weekly days are in one row on the fresh screenshot; desktop repeater one-row contract is automated; phone no-overflow tests pass.
  - Validation evidence: targeted task-card UI tests passed; headless smoke passed; ReadmeMedia weekly PNG/report passed with no warnings; affected project builds passed.
  - Unsupported claims: no claim of full solution build/test pass; repeated full solution build hang is reported below.
  - Regression / edge case: Russian weekday labels use short resources (`Пн` ... `Вс`) with full weekday tooltips; phone widths remain checked.
  - Comments/docs/changelog: no production comments or changelog needed.
  - Hidden contract change: automation ids preserved; visible weekday labels shortened intentionally to satisfy compact desktop layout, with full labels preserved in tooltips.
  - Manual-review challenge: the likely challenge was exactly the one raised by the user, Sunday wrapping; it is now explicitly checked by screenshot and test.
- No-findings justification: the latest counterexample is fixed and verified; residual issues are only environment/evidence limitations with next-best checks.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | MP4 recording attempt remains blocked because ReadmeMedia creates short-lived FlaUI windows and the recorder could not attach to `Unlimotion README` before window closure; PNG/report evidence is stable | Use PNG/report fallback evidence and report blocker | accepted-risk |
| LOW | validation | Repeated `dotnet build .\src\Unlimotion.sln --no-restore -nr:false -m:1 -p:UseSharedCompilation=false -v:minimal` produced no output and timed out after ~420s; fresh `dotnet`/`MSBuild` processes from the attempt were stopped | Use passed affected project builds plus targeted UI/smoke tests; report blocker | accepted-risk |

- Fixed before final report: weekday buttons now use compact fixed-width short labels and the weekly desktop test fails if any day wraps to another row.
- Checks rerun: `dotnet build .\src\Unlimotion.Test\Unlimotion.Test.csproj --no-restore -nr:false -m:1 -p:UseSharedCompilation=false -v:minimal`; `dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj --no-build --no-restore -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --no-progress`; `dotnet build .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj --no-restore -nr:false -m:1 -p:UseSharedCompilation=false -v:minimal`; `dotnet run --no-build --project .\tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review task-card --language ru --output-root artifacts\ux-review\20260602-235713-task-card-weekly-confirmation --no-build-before-launch`; `dotnet run --no-build --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*" --no-progress`; `git diff --check`; repeated full solution build attempt timed out.
- Validation evidence: weekly PNG `artifacts/ux-review/20260602-235713-task-card-weekly-confirmation/desktop/repeater-planning.png`; report `artifacts/ux-review/20260602-235713-task-card-weekly-confirmation/report.json` with `Warnings: []`.
- Unrelated changes: no unrelated tracked source changes identified; generated artifacts are local and ignored by git status.
- Needs human: optional visual judgment of final PNG only; no implementation decision is blocked.
- Residual risks / follow-ups: full solution build/test can be retried after resolving the build runner hang; MP4 evidence can be retried with a recorder harness that can pin the short-lived ReadmeMedia window.

## Approval
Получено: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создание рабочей спецификации | 0.90 | Подтверждение пользователя на EXEC | Запросить `Спеку подтверждаю` | Да | Да, запрос подтверждения будет отправлен после создания spec | QUEST требует SPEC-first и явное подтверждение до изменения кода | `specs/2026-06-02-task-card-desktop-planning-repeater-polish.md` |
| SPEC | Итеративный spec review | 0.94 | Подтверждение пользователя на EXEC | Передать spec после review | Да | Да, пользователь попросил review до only LOW findings | Закрыты HIGH/MEDIUM findings по неверной UX capture command и слабому video fallback; остались только LOW tradeoffs | `specs/2026-06-02-task-card-desktop-planning-repeater-polish.md`, `tests/Unlimotion.ReadmeMedia/Program.cs` |
| EXEC | Spec approval intake and implementation orientation | 0.92 | Нет | Добавить regression assertions and layout fix | Нет | Да, пользователь подтвердил spec | Подтверждение сняло QUEST SPEC stop rule; текущий scope ограничен task-card planning/repeater layout | `specs/2026-06-02-task-card-desktop-planning-repeater-polish.md`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | Layout implementation and regression coverage | 0.93 | Нет | Run targeted UI tests | Нет | Нет | Desktop regular planning/repeater metrics and localized short weekday labels address the user screenshot defects while tests assert row placement and compact toggles | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.ViewModel/Resources/Strings.resx`, `src/Unlimotion.ViewModel/Resources/Strings.ru.resx`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | Targeted UI and visual validation | 0.91 | Full solution build/test blocked by hang; MP4 blocked by short-lived capture window | Post-EXEC review and final report | Нет | Нет | Targeted task-card UI tests, headless smoke, affected project builds, ReadmeMedia PNG/report and diff check passed; full build/video blockers recorded | `artifacts/ux-review/20260602-221600-task-card/`, `artifacts/ux-review/20260602-222031-task-card-video/`, `specs/2026-06-02-task-card-desktop-planning-repeater-polish.md` |
| EXEC | Weekly repeater screenshot correction | 0.94 | Нет | Final report | Нет | Да, пользователь указал, что `Вс` всё ещё переносится на второй ряд | Added explicit one-row weekday assertion, confirmed fresh weekly screenshot keeps `Пн`...`Вс` in one row, and reran targeted UI/smoke checks | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `artifacts/ux-review/20260602-235713-task-card-weekly-confirmation/`, `specs/2026-06-02-task-card-desktop-planning-repeater-polish.md` |
