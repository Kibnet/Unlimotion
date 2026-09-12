# Эмодзи родительской цепочки в карточке задачи

## Цель и границы

- Metadata: owner — Codex agent `/root`; форма — Short по `quest-governance` (один локальный обратимый UI-outcome, без миграций, хранилища, публичного API или внешних side effect); профиль — `dotnet-desktop-client` + `ui-automation-testing`; context — `testing-dotnet`, `visual-feedback`, `session-insights`; центральный stack: `creator-vibe-lens`, `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `tool-execution-baseline`, `spec-linter`, `spec-rubric`, `review-loops`; локальное ужесточение: `AGENTS.override.md` требует UI coverage.
- AS-IS: карточка показывает статус, заголовок, «Важное», важность, ID, команды и даты. Родительские задачи видны только в нижней секции отношений. В модели уже вычисляется `GetAllEmoji`: для задач с родителями это эмодзи всей цепочки предков, но для корневой задачи это её собственный эмодзи.
- Проблема: контекст задачи в иерархии обнаруживается слишком поздно, а прямой вывод `GetAllEmoji` в шапке создал бы ложный блок «родителей» у корневой задачи.
- Outcome: у задачи с эмодзи в родительской цепочке появляется компактный trail сразу под заголовком; он не занимает место заголовка и не создаёт пустой зазор у задач без таких эмодзи.
- Non-Goals / ограничения: не менять связи задач, порядок существующих контролов, навигацию, фильтрацию, локализацию, формат хранения или список эмодзи; не делать trail кликабельной навигацией; не добавлять отдельную большую секцию «Родители»; не коммитить screenshots/video по умолчанию.
- Effective runtime: Avalonia desktop UI и существующий TUnit/Avalonia.Headless suite. Проверка ограничена целевыми UI tests и скриншотами по явному ранее заданному правилу пользователя для маленькой UI-фичи; полный suite/полная сборка не входят в этот scope и будут честно отмечены как не запущенные.

## Результат, решения и проверки

Визуальный план (desktop; на узком окне `WrapPanel` переносит блок вместе с другими метаданными):

```text
[ статус ]  Название задачи
[ Важное ][ важность ][ 🧭 🛠 — родители ][ ID ][ ⚙ ][ даты ]
                                   ^ компактный контекст, не новый раздел
```

| Observable scenario / решение (owner) | AC / ожидаемый результат | Команда / evidence |
| --- | --- | --- |
| Пользователь открывает дочернюю задачу, у которой у родителей/предков есть эмодзи. Решение агента: показывать предсказуемо упорядоченную цепочку всех предков от корня к непосредственному родителю, а не только одного прямого родителя. | Перед ID и до меню команд виден `CurrentTaskParentEmojiTrail` с этими эмодзи. Он использует `EmojiTextBlock`, поэтому эмодзи сохраняют штатный шрифт. У блока есть локализованное доступное имя/tooltip «Родительские задачи». | Targeted headless UI test в `MainControlTaskCardLayoutUiTests`: fixture с дочерней задачей и двумя подписанными эмодзи родителями; проверяет текст, доступность, видимость и геометрию `trail → ID → actions`. |
| Пользователь открывает корневую задачу либо задачу, у чьих родителей нет эмодзи. Решение агента: отдельный `ParentEmojiTrail` не наследует fallback собственного эмодзи из `GetAllEmoji`. | `CurrentTaskParentEmojiTrail` скрыт; собственного эмодзи задачи в нём нет; не остаётся пустого отступа. | Targeted UI test проверяет скрытый/неразмещённый block у задачи без parent-emojis. |
| Пользователь смотрит карточку на desktop и телефоне. Решение агента: trail — элемент уже существующей `TaskHeaderStateRow` перед ID, а не строка заголовка и не нижняя relation-section. | Заголовок сохраняет всю доступную ширину. Переключатель статуса остаётся на своей вертикали; trail не выходит за пределы card/scroll viewport и допускает перенос вместе с метаданными. | Desktop и 360/390/430 px cases существующего `MainControlTaskCardLayoutUiTests`; screenshot evidence с дочерней задачей и видимым trail. |

- Decision ledger:
  - Место: строка метаданных под заголовком, **после важности и перед ID**. Это прямое уточнение пользователя 2026-09-13: контекст читается раньше служебного идентификатора, но не конкурирует с редактированием названия; нижняя секция отношений остаётся местом редактирования графа.
  - Содержание: только эмодзи цепочки предков, без текста названий и без новых действий. Tooltip/accessibility дают семантику; полный состав отношений остаётся в существующем разделе.
  - Источник: новый явный computed-property `ParentEmojiTrail` и `HasParentEmojiTrail`, вычисленный через существующий cycle-safe `GetAllParents()` и обновляемый вместе с `GetAllEmoji`. Не использовать `GetAllEmoji` напрямую, так как его fallback содержит собственный emoji корня.
  - Порядок: существующая deterministic traversal `GetAllParents()` (сверху вниз, `Title`-order на развилках); дубликаты не удаляются, потому что каждый представляет отдельную задачу в графе.
- Изменяемые файлы / ответственность:
  - `src/Unlimotion.ViewModel/TaskItemViewModel.cs` — вычисление и notifications только для parent trail; отношения и `GetAllEmoji` contract сохраняются.
  - `src/Unlimotion/Views/MainControl.axaml` — один условно видимый `EmojiTextBlock`, AutomationId, tooltip/name и минимальный стиль в header metadata row.
  - `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` — expected-red затем regression UI coverage для trail, negative case и responsive geometry.
  - `specs/2026-09-12-task-card-parent-emojis.md` — журнал и review evidence.
- Errors / recovery / rollback: input пустой, отношения отсутствуют, циклы и дубли обрабатывает существующий `GetAllParents()`; экран не должен падать, а trail скрывается. Rollback — удалить три UI/model/test изменения одним обратимым commit; task JSON и task graph не мигрируются.
- Performance / compatibility: вычисление повторно использует уже выполняемый traversal; добавляется одна короткая string property и один element только при содержимом. JSON/schema/API/локализация не меняются; все существующие stable AutomationId сохраняются.
- Expected user review objections и устранение:
  - «Блок отнимает ширину у названия» — он в строке под названием, не в title-grid.
  - «У корневой задачи показывается её собственный эмодзи как родительский» — используется отдельный trail без fallback.
  - «На телефоне всё уезжает» — `WrapPanel` и UI geometry checks на 360/390/430 px, плюс реальный screenshot.
  - «Непонятно, что означают эмодзи» — tooltip/accessibility «Родительские задачи» и существующая relation-section для подробностей.
- Открытые вопросы: нет. Решение «вся цепочка предков» зафиксировано как наиболее полезный и согласованный с существующим `GetAllEmoji` вариант; подтверждение этой SPEC подтверждает его.
- Plan / stop rules:
  1. После exact `Спеку подтверждаю` добавить failing UI assertion без product-code изменения; удостовериться, что падает именно из-за отсутствия trail.
  2. Внести минимальные model/XAML changes и восстановить green targeted UI test.
  3. Запустить затронутый UI class serially с `--treenode-filter` и `-p:UseSharedCompilation=false`; остановиться при unrelated/infra failure, не повторять идентичную команду без новой гипотезы.
  4. Собрать и визуально проверить screenshots (desktop и узкое окно); video fallback — existing harness для этой карточки не предоставляет безопасной автоматической видеозаписи, поэтому сохраняются inspected PNG и лог targeted UI run.
  5. Выполнить post-EXEC review, обновить задачу `3-A` результатами и только затем перевести её в Completed. Commit/push/PR — только по отдельному запросу пользователя.

## Quality gate и review

### Linter

| № | Verdict | Evidence / остаточный риск |
| --- | --- | --- |
| 1 | PASS | Наблюдаемый header trail и пустой negative state определены. |
| 2 | PASS | Проверены `MainControl.axaml`, `TaskItemViewModel`, fixture и текущий header order. |
| 3 | PASS | Зафиксированы поздний контекст и ложный root fallback `GetAllEmoji`. |
| 4 | PASS | Близость к названию без конкуренции за title-width, отсутствие пустого места и responsive layout. |
| 5 | PASS | Non-Goals явно исключают graph/navigation/storage/localization changes. |
| 6 | PASS | Ownership каждого планируемого файла задан. |
| 7 | PASS | Model computed property → XAML binding → stable AutomationId → UI test. |
| 8 | PASS | Traversal order, cycle handling, root negative case и visibility invariant определены. |
| 9 | PASS | Empty/malformed semantic state не вызывает exception; скрытие и rollback определены. |
| 10 | PASS | Переиспользуется existing traversal; дополнительная цена ограничена string/element. |
| 11 | PASS | Нет нового persisted state; указано derived state. |
| 12 | PASS | Нет schema/API/localization migration. |
| 13 | PASS | Узкий обратимый rollback без данных. |
| 14 | PASS | AC включают positive, negative, order, accessibility и narrow-screen state. |
| 15 | PASS | Каждому AC сопоставлен конкретный UI test/evidence. |
| 16 | PASS | Реальные runner flags, visual evidence и stop rules указаны. |
| 17 | PASS | Последовательность TDD → implementation → targeted UI → screenshot → review определена. |
| 18 | PASS | Decision ledger заполнен, user-owned blocker отсутствует. |
| 19 | PASS | Short eligibility обоснована. |
| 20 | PASS | Avalonia/TUnit/UI automation/visual-feedback requirements отражены. |

Итог linter: **ГОТОВО** — в A/C/D нет FAIL, неразрешённых HIGH/MEDIUM нет.

### Rubric

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель / границы | 5 | Один конкретный UI outcome и запреты на расширение scope. |
| AS-IS | 5 | Исследованы реальные XAML, model semantics и test fixture. |
| Конкретность дизайна | 5 | Визуальный план, место, source, order, visibility и selectors определены. |
| Безопасность / миграция / rollback | 5 | Нет внешних данных; compatibility и обратимый rollback явно заданы. |
| Проверяемость | 5 | Positive/negative/responsive AC связаны с UI test и screenshots. |
| Автономность решений | 5 | Material user-owned choice отсутствует; all-ancestors decision закреплён для approval. |

Итог: **30/30, готово к автономной реализации после exact approval**.

### Post-SPEC review

- Статус: **PASS**.
- Scope reviewed: эта SPEC; `TaskItemViewModel.cs`; header/styles/relation section в `MainControl.axaml`; `EmojiTextBlock.cs`; `MainControlTaskCardLayoutUiTests.cs`; fixture snapshots; current clean worktree at `3aa24c8f`; instruction stack и local `AGENTS.override.md`.
- Review passes:
  - Scope/Evidence: planned files ограничены model/XAML/one test; existing `GetAllEmoji` root fallback подтверждён кодом.
  - Contract: placement и responsive contract не изменяют title/status/relations; model state remains derived and localized existing `ParentsTasks` resource reused.
  - Adversarial risk: проверены empty parents, root with own emoji, parents without emoji, multiple ancestors, duplicate icons, narrow widths и potential false semantics from `GetAllEmoji`; isolated parent trail resolves root false positive.
  - Role-based: UX/designer — PASS (header hierarchy preserved); Tester/validation — PASS (positive/negative/responsive evidence planned); Developer/architect — PASS (no schema/API, cycle-safe existing traversal); Business/domain — Не применимо, task graph semantics not altered; Delivery/operations/security — Не применимо, no CI/config/release/external state change.
  - Fix and re-review: initial idea to bind `GetAllEmoji` was rejected before SPEC because it shows a root task's own emoji; current dedicated trail reviewed again.
  - Stop decision: можно запрашивать подтверждение.
- Evidence inspected: code paths and fixture described above; no code diff exists at SPEC phase except this spec.
- Depth checklist: scope drift — none; AC — positive/negative/responsive; decision ledger/scenarios/objections — complete; validation — targeted UI + PNG fallback explicit; unsupported claims — none; regression/edge — root fallback and narrow layout covered; comments/docs/changelog — no change; hidden contract — no API/storage change; manual-review challenge — direct test must demonstrate trail does not equal own root emoji.
- No-findings justification: the only material risk found (root fallback) has an explicit design correction and regression criterion.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | Evidence | Harness has no established safe automated video artifact for this card. | Preserve targeted UI log and inspected timestamped PNG screenshots; report video fallback honestly. | accepted-risk |
| LOW | UX | Emoji-only trail is intentionally compact and not a full breadcrumb. | Preserve tooltip/accessibility and relation section; do not expand scope to navigation. | accepted-risk |

- Checks rerun: manual linter criteria 1–20 and rubric after root-fallback correction; post-SPEC review after correction.
- Needs human: exact approval only.
- Residual risks / follow-ups: visual density with a very long chain is bounded by `WrapPanel`; truncation/capping is not introduced without observed evidence.

### Поправка после закрытия

- Пользователь прямо запросил перенести trail слева от ID и перебазировать работу на актуальный `main`. Это локальная правка уже подтверждённого UI-outcome без изменения модели, данных, API или Non-Goals; отдельный approval gate не требуется. Старый post-EXEC PASS становится устаревшим до повторных UI checks и свежих screenshots.

### Post-EXEC review

- Статус: **PASS**.
- Scope reviewed: approved SPEC; `TaskItemViewModel.cs`, `MainControl.axaml`, `MainControlTaskCardLayoutUiTests.cs`; `git status --short`, relevant diff and `git diff --check`; targeted TUnit report; inspected desktop/phone PNG and `report.json` from UX harness.
- Review passes:
  - Scope/Evidence: diff ограничен computed property, one header control/style, two focused UI tests and the existing responsive test extension; no task storage/schema/localization/navigation diff.
  - Contract: ancestor trail is after importance/before ID, root own emoji is hidden, `EmojiTextBlock` keeps emoji font, existing status/title/relation sections/selectors stay intact.
  - Adversarial risk: expected-red failure was specifically missing AutomationId; passing tests cover root self-emoji, multi-parent ancestor string, desktop order and 360/390/430 containment. UX PNG confirms the same visible states in a real FlaUI-launched window.
  - Role-based: UX/designer — PASS (trail is visually subordinate to title and does not add a section); Tester/validation — PASS (22/22 affected UI class and real-window PNG); Developer/architect — PASS (derived model state, no persisted contract); Business/domain — Не применимо (relations semantics unchanged); Delivery/operations/security — Не применимо (no config/CI/release/PR operation).
  - Fix and re-review: responsive fixture was initially attached to the create-menu test by broad patch context; it was restored and moved to the task-card test before the green class run. Re-review covered the corrected diff and 360/390/430 results.
  - Stop decision: можно завершать и закрывать задачу.
- Evidence inspected:
  - expected red: one targeted test failed because `CurrentTaskParentEmojiTrail` was absent;
  - green: `dotnet run -p:UseSharedCompilation=false --project src\\Unlimotion.Test\\Unlimotion.Test.csproj -- --treenode-filter '/*/*/MainControlTaskCardLayoutUiTests/*' --maximum-parallel-tests 1 --output Detailed` — 22/22 passed, 1m01.847s;
  - real window: `dotnet run -p:UseSharedCompilation=false --project tests\\Unlimotion.ReadmeMedia\\Unlimotion.ReadmeMedia.csproj -c Debug -- --ux-review task-card --language ru --output-root artifacts\\ux-review\\20260913-parent-emojis`; inspected `desktop/repeater-planning.png`, `phone/repeater-planning-card.png`, `desktop/root-description.png`; no capture warnings.
- Depth checklist: scope drift/unrelated changes — none; AC — all covered; user-visible scenarios/AC matrix/objections — closed; validation — targeted UI and real-window screenshots present; unsupported claims — none; regression/edge — root, empty trail and narrow widths covered; comments/docs/changelog — no change; hidden contract — no storage/API change; manual-review challenge — verified the root screenshot does not render the root's own rocket as parent context.
- No-findings justification: all concrete risks have a matching test and inspected visual state; no remaining BLOCKER/HIGH/MEDIUM.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | Evidence | Existing task-card capture harness records PNG, not a safe automated video. | Keep the inspected real-window PNG paths and targeted TUnit report; do not claim video evidence. | accepted-risk |

- Fixed before final report: corrected accidental test-fixture placement before green run.
- Checks rerun: full affected UI class after correction; then, after the user-requested placement amendment and rebase, one focused order test and a fresh real-window UX capture.
- Validation evidence: 23/23 affected class in 1m22.226s after review fixes; explicit long-chain responsive test 3/3 at 360/390/430; timestamped desktop and phone PNGs under `artifacts/ux-review/20260913-parent-emojis-review-fixes`.
- Unrelated changes: none; artifacts are generated inspection output and not staged/committed.
- Needs human: none.
- Residual risks / follow-ups: extremely long ancestor chains are visually capped at 120 px with ellipsis; the complete relationship remains available in the existing relation section. Full solution suite was not run by the explicit narrow UI validation scope.

## Approval

Ожидается точная фраза: **«Спеку подтверждаю»**. Фазовые правила — `C:\Users\Kibnet\.codex\agents\instructions\core\quest-mode.md`.

## Журнал действий агента

| Фаза / блок | Решение / уверенность | Evidence / что неизвестно | Следующий шаг | Передача человеку / фактическое решение |
| --- | --- | --- | --- | --- |
| SPEC: discovery | 0.95 — header metadata placement является наилучшим компромиссом | Проверены current XAML/header, model `GetAllEmoji`/`GetAllParents`, EmojiTextBlock и UI fixture; реальный rendered result пока неизвестен | Подготовить short SPEC | Ожидается пользовательское подтверждение placement и scope через exact approval |
| SPEC: safety correction | 0.99 — нужен отдельный `ParentEmojiTrail` | `GetAllEmoji` у root возвращает `Emoji`, то есть собственный emoji | Исключить direct binding и зафиксировать negative AC | Решение принято агентом как objectively necessary correctness fix; входит в approval scope |
| SPEC: review | 0.95 — можно переходить к approval | Linter 20/20 PASS, rubric 30/30, post-SPEC PASS; two LOW fallback risks documented | Ждать exact approval | Ожидается «Спеку подтверждаю» |
| EXEC: approval | 1.00 — SPEC подтверждена | Пользователь написал точную фразу «Спеку подтверждаю» 2026-09-13 | Добавить expected-red UI regression test | Approval получен; code scope ограничен утверждённой SPEC |
| EXEC: expected red | 1.00 — regression test корректно характеризует gap | Targeted TUnit: 1 failed; `CurrentTaskParentEmojiTrail` not found | Реализовать approved trail и negative/responsive checks | Решение не требуется |
| EXEC: implementation | 0.95 — минимальный model/XAML/test change внесён | Отдельный trail не использует root fallback `GetAllEmoji`; добавлены positive, root-negative и 360/390/430 checks | Запустить targeted UI class | Решение не требуется |
| EXEC: validation | 1.00 — UI contract подтверждён | 22/22 affected TUnit; real FlaUI desktop/phone capture без warnings, PNG inspected | Выполнить post-EXEC review и закрыть task `3-A` | Решение не требуется |
| EXEC: review | 0.98 — завершение допустимо | Post-EXEC PASS; video fallback и long-chain wrap отмечены как LOW/residual | Записать результат в task description, `complete` через CLI | Закрытие task authorised initial workflow |
| EXEC: amendment | 1.00 — пользователь явно выбрал новый порядок `trail → ID` | Placement перед ID заменяет только визуальный порядок; `origin/main` обновлён до `e657843e` | Разрешить test conflict, применить order, повторить UI evidence | Новая инструкция пользователя является авторизацией изменения |
| EXEC: amendment validation | 1.00 — amended UI contract подтверждён | Rebase на `e657843e` завершён; конфликт UI-теста разрешён с сохранением проверки выравнивания title; 22/22 affected class, 1/1 explicit order и новые PNG inspected | Обновить описание и завершить `3-A` через CLI | Решение не требуется |
| EXEC: PR review fixes | 1.00 — оба P2 закрыты в scope существующего outcome | Переименование предка обновляет all descendants через cycle-safe обход; trail ограничен 120 px и `CharacterEllipsis`; UI test строит восемь уникальных связанных предков | Перебазировать на `0b6e897d`, обновить PR и пометить discussions resolved | Решение не требуется |
