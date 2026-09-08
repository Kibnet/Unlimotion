# Визуальная согласованность popup выбора emoji с popup фильтров

## Цель и границы

- Metadata:
  - источник: задача Unlimotion `9b8d308b-11a4-4879-8e00-0c22125f65fb` — «Добавить в попап выбора эмодзи фон, рамку и закругление углов, чтобы он был как и у попапа фильтров»;
  - owner результата: пользователь; owner реализации после approval: агент, закреплённый в задаче;
  - форма: Short — один локальный, обратимый visual outcome; нет изменений данных, config, security, публичного API, миграции или внешнего side effect;
  - instruction stack: central `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `tool-execution-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `spec-linter`, `spec-rubric`, `review-loops`; локальный `AGENTS.override.md`;
  - product intent: не создавать новый дизайн, а привести существующий popup emoji-фильтра к уже принятому chrome popup фильтров.
- Проверенный AS-IS:
  - `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml`, стиль `Border.EmojiFilterDropDown`, уже использует `ThemeBackgroundBrush`, `ThemeControlMidBrush` и `BorderThickness=1`, но `CornerRadius=4`;
  - `src/Unlimotion/Views/MainControl.axaml` и `src/Unlimotion/Views/GraphControl.axaml`, стиль `Border.FilterOverflowPanel`, используют те же background/border значения, `BorderThickness=1` и `CornerRadius=6`;
  - существующий headless-сценарий `MainControlFilterToolbarResponsiveUiTests.EmojiScenarios.Toolbar_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing` закрепляет старое значение `CornerRadius=4`, но не сравнивает весь внешний chrome с popup фильтров;
  - `git blame` подтверждает, что background/border emoji popup добавлены в `3bd0983d0`, radius `4` — в `b3c55aaf2`, а reference radius `6` существует в `FilterOverflowPanel` с `a103b106e`;
  - текущий checkout `feat/daily-feed` содержит unrelated пользовательские изменения. EXEC выполняется в отдельном worktree от текущего `HEAD`; эти изменения не переносятся и не редактируются.
- Корневая проблема: два соседних popup одного toolbar имеют почти одинаковый chrome, но radius задан независимо и уже расходится (`4` против `6`); тест закрепляет значение одного popup вместо сравнения с визуальным reference.
- Наблюдаемый outcome: открытые popup emoji-фильтра и фактический `FlyoutPresenter` общих фильтров используют одинаковые background, border brush, border thickness и corner radius в светлой и тёмной теме; содержимое, размеры и поведение popup не меняются.
- Уточнение после desktop screenshot и повторной обратной связи: parity относится к фактически видимому chrome. Popup и список должны иметь непрозрачный theme background, а внешний rounded `Border` должен обрезать дочернее содержимое по своим углам; совпадение только computed-свойств внешнего `Border` недостаточно.
- Effective runtime для SPEC: Codex subagent в локальной Windows-среде; репозиторий Avalonia/.NET; `global.json` и `dotnet --version` разрешают SDK `10.0.400`; TUnit/Microsoft.Testing.Platform. Реальный EXEC runtime и commit фиксируются в журнале перед изменениями.

### Non-Goals и ограничения

- Не менять padding, max height, размещение popup, light-dismiss, keyboard/pointer flow, список emoji, фильтрацию, empty state или exclude styling.
- Не перерабатывать общую theme/style architecture и не выносить новый глобальный resource только ради четырёх свойств.
- Не менять automation IDs, bindings, ViewModel/domain/storage/API.
- Не менять `README.md`, `CHANGELOG.md`, product docs и другие задачи.
- Не выполнять push, merge, deploy, release или публикацию.
- Кодовый EXEC был начат только после точной фразы `Спеку подтверждаю` и отдельного разрешения на минимальный repair task graph.

### Visual planning artifact

Текстовый wireframe является достаточным planning artifact: меняется только внешний радиус существующей рамки на два пикселя; layout/content/state не меняются. Он хранится прямо в SPEC и доступен reviewer.

```text
AS-IS                                      TO-BE

emoji popup          filter popup          emoji popup          filter popup
╭ radius 4            ╭ radius 6            ╭ radius 6            ╭ radius 6
│ bg: ThemeBackground │ bg: ThemeBackground │ bg: ThemeBackground │ bg: ThemeBackground
│ border: Mid / 1 px  │ border: Mid / 1 px  │ border: Mid / 1 px  │ border: Mid / 1 px
╰                     ╰                     ╰                     ╰

Внутренний список, поиск, размеры, placement и взаимодействия: без изменений.
```

## Результат, решения и проверки

### User-observable scenarios, Decision Ledger и Acceptance-to-Test Matrix

| Observable scenario / решение (owner) | AC / ожидаемый результат | Команда / evidence |
| --- | --- | --- |
| Пользователь открывает emoji popup рядом с popup фильтров в светлой теме. Решение: reference — фактически рисующий chrome `FlyoutPresenter`, а не внутренний `FilterOverflowPanel` с неразрешёнными legacy resource keys (agent после RED). | AC1: computed `Background`, `BorderBrush`, `BorderThickness`, `CornerRadius` emoji popup равны свойствам реального `FlyoutPresenter`. | Headless regression в `MainControlFilterToolbarResponsiveUiTests`; RED/GREEN на реальном rendered control tree. |
| Пользователь визуально различает цельный непрозрачный popup, рамку и скруглённые углы. Решение: внешний `Border` обрезает непрозрачный список по своему `CornerRadius` (user correction + agent implementation choice). | AC1b: `EmojiFilterDropDown` и `EmojiFilterList` используют один непрозрачный `FlyoutPresenterBackground`; `EmojiFilterDropDown.ClipToBounds=true`; placement, размеры и padding не меняются. | Падающий headless assertion на clipping и opacity, затем screenshot открытого popup. |
| Пользователь переключает приложение в тёмную тему и снова открывает оба popup. Решение: сохранить существующие dynamic theme resources (agent). | AC2: background/border берутся из существующих dynamic resources и остаются читаемыми после смены темы; hard-coded color не появляется. | Headless test на обе темы либо один параметризованный test; дополнительно inspection XAML diff. |
| Пользователь продолжает искать и выбирать emoji после визуальной правки. | AC3: open/search/toggle/close flow, placement, max height и light-dismiss не изменены. | Существующий `Toolbar_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing` и весь `MainControlFilterToolbarResponsiveUiTests`. |
| Пользователь открывает popup в узком окне. | AC4: popup остаётся внутри viewport; размеры и содержимое не меняются. | `Toolbar_EmojiFilters_PopupStaysVisibleInNarrowViewport`. |
| Reviewer проверяет scope. Решение: минимальная правка существующего style плюс regression test (agent). | AC5: diff ограничен target XAML, релевантным headless test и журналом этой SPEC; automation IDs и production logic не меняются. | `git status --short`, `git diff --stat`, `git diff -- <three paths>`, `rg` по automation IDs. |
| Выполнение завершается. | AC6: affected build, targeted tests, полный `Unlimotion.Test` и полный `Unlimotion.UiTests.Headless` зелёные. | Команды staged validation ниже; exit `0`, непустое число тестов. |

### Дизайн изменения и ответственность файлов

| Файл | Ответственность | Планируемая правка |
| --- | --- | --- |
| `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml` | внешний chrome emoji popup | Согласовать `CornerRadius` `EmojiFilterDropDown` с reference `FilterOverflowPanel`; существующие dynamic background/border и размеры сохранить. |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | user-level headless regression | Добавить/расширить сценарий, который открывает оба реальных popup и сравнивает весь chrome; заменить старое самостоятельное ожидание radius `4` на reference-based ожидание. |
| `specs/2026-09-08-task-9b8d308b-emoji-popup-chrome.md` | QUEST audit | Обновлять журнал SPEC/EXEC, проверки, review findings и фактический outcome. |

- Интеграция: `EmojiFilterMultiSelectSearchBox` используется в task tabs и Roadmap; reference `FilterOverflowPanel` существует в `MainControl.axaml` и `GraphControl.axaml`. Production logic/ViewModel не затрагиваются.
- Инвариант: source of visual truth в тесте — computed свойства фактического `FlyoutPresenter` в том же rendered control tree; внутренний content panel не считается chrome, если его resource key резолвится в `null`.
- Инвариант видимой поверхности: внешний `EmojiFilterDropDown` и внутренний `EmojiFilterList` используют один непрозрачный theme background, а внешний border обрезает содержимое по своему rounded bounds.
- Ошибки/recovery: если headless harness не может одновременно materialize оба popup, test сначала получает reference `Border` из detached Flyout content и computed style после attachment; отсутствие materialization считается test-design failure, а не основанием ослабить AC. При неоднозначном visual target EXEC останавливается без расширения scope.
- Данные/состояние: persistent data и task model не меняются. Временное состояние theme/popup в тесте восстанавливается в `finally` по существующему fixture pattern.
- Performance: неприменимо — меняется одно style value и несколько assertions; новых subscriptions, layout containers или runtime вычислений нет.
- Совместимость/миграция: storage/API/format migration отсутствует; проверяются task-list toolbar и Roadmap, light/dark theme.
- Rollback: вернуть одно style value и соответствующее ожидание test; отдельный isolated-worktree commit/diff делает откат точным.

### План EXEC и stop rules

1. После двух разрешений ниже создать/использовать отдельный clean worktree от зафиксированного commit и перечитать target files.
2. Исправить task graph минимальным Git-восстановимым patch, выполнить `unlimotion-cli validate`, записать в описание agent/run marker и перевести задачу в `InProgress`; перечитать authoritative task. Если любой write неоднозначен — остановиться.
3. Добавить regression test сравнения chrome и выполнить targeted RED. RED засчитывается только если ожидаемое единственное различие — radius `4 != 6`; compile/runner/fixture failure не считается TDD evidence.
4. Изменить только target radius; обновить старое ожидание test на reference-based contract.
5. Выполнить staged validation, visual evidence fallback и full post-EXEC review. При любом failing обязательном test не переводить задачу в `Completed`.
6. После доказанного результата дописать task description (agent/run, итог, files, checks, unverified/risk), перевести задачу в `Completed`, перечитать task и повторить graph validation.

Stop rules:

- Нет `Спеку подтверждаю` или нет явного разрешения на graph repair — только SPEC, без code/task writes.
- Task уже не `Prepared`, появился другой agent marker или commit/task content изменился — остановиться и вернуть конфликт.
- RED падает не по chrome parity или выявляется другой emoji popup — остановиться и запросить уточнение вместо redesign.
- Любая необходимость менять shared theme architecture, popup behavior или более двух production/test files — обновить SPEC и повторить approval gate.
- Full test suite, relevant UI test или graph validation не зелёные — оставить task `InProgress`, вернуть partial/blocker.

### Exact validation contract

Preflight в isolated worktree:

```powershell
dotnet --version
git rev-parse --show-toplevel
git branch --show-current
git status --short
dotnet test --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug -- --list-tests
```

TDD RED и targeted GREEN:

```powershell
dotnet test --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/Toolbar_EmojiFilterPopup_UsesFilterFlyoutChrome*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
dotnet test --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
```

Affected build и full suites:

```powershell
dotnet build src\Unlimotion.sln -c Debug -p:UseSharedCompilation=false
dotnet test --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --maximum-parallel-tests 1 --minimum-expected-tests 1 --output Detailed
dotnet test --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -c Debug -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --minimum-expected-tests 1 --output Detailed
```

- Команды выполняются последовательно из-за общего Avalonia/UI state и output directories.
- До long full suites фиксируются results/log paths и repo-history duration; timeout не выбирается произвольно.
- Video fallback: `SafeHeadlessUnitTestSession` не предоставляет recorder, а отдельного FlaUI сценария для emoji popup нет. Поэтому video помечается `Не применимо`; next-best evidence — deterministic computed-property headless regression, существующие interaction/viewport tests и, если desktop window доступно, локальные before/after screenshots в `chat-artifacts/emoji-popup-chrome/` без коммита.

### Риск, objections и открытые решения

| Expected user review objection | Ответ / проверка |
| --- | --- |
| «Background и border уже есть — зачем менять всё?» | Production diff меняет только остающееся расхождение radius; test при этом защищает все четыре свойства от будущего drift. |
| «Не превратится ли маленькая правка в redesign popup?» | Non-Goals запрещают изменение content, padding, size, placement, behavior и theme architecture; diff gate ограничивает scope. |
| «Будет ли это работать в обеих темах и Roadmap?» | Сохраняются dynamic resources; тест параметризуется по theme и проверяет оба места использования либо общий control contract плюс Roadmap smoke. |
| «Можно ли считать работу завершённой по XAML diff?» | Нет: обязательны expected RED, targeted GREEN, full suites и UI/headless evidence. |
| «Почему на screenshot popup просвечивает, а углы снова квадратные?» | Прозрачность внутреннего списка была ошибочным исправлением. Новый контракт требует непрозрачный background и rounded clipping дочернего содержимого внешним `Border`. |
| «Не потеряются ли мои текущие изменения в `feat/daily-feed`?» | EXEC только в отдельном worktree от зафиксированного commit; dirty checkout не редактируется. |

User-owned решение было получено перед EXEC: разрешён минимальный repair task graph, чтобы CLI смог безопасно закрепить задачу:

1. удалить tracked zero-byte tombstone `C:\Projects\Education\Unlimotion Space\Tasks\236a9212-3c5b-4b25-9c5d-432f7cf18e01` (`git rm` в task repo; восстановление возможно из Git history);
2. предполагалось в задаче `eadd8c1a-bf03-46e4-a56c-b16408f1022b` добавить `62b50b0e-2608-45d0-818e-5baf583b74ac` в `BlocksTasks`, сохранив существующую прямую ссылку `62b... BlockedByTasks -> eadd...`;
3. проверить `git diff` только этих двух task paths и выполнить `unlimotion-cli validate` до status/description writes.

Предпочтённый combined approval:

> Спеку подтверждаю. Разрешаю минимально исправить граф задач: удалить нулевой tombstone `236a9212-3c5b-4b25-9c5d-432f7cf18e01` и восстановить обратную связь `eadd8c1a-bf03-46e4-a56c-b16408f1022b BlocksTasks -> 62b50b0e-2608-45d0-818e-5baf583b74ac`.

Перед write повторная authoritative проверка обнаружила drift: `62b...BlockedByTasks=[]` и `eadd...BlocksTasks=[]`. Поэтому второй разрешённый шаг стал неприменим и был безопасно пропущен; несуществующая прямая связь не создавалась заново. Выполнено только удаление tracked zero-byte tombstone, после чего actual graph validation стал green (`taskCount=2857`, `isValid=true`).

## Quality gate и review

### SPEC Linter 1..20

| № | Verdict | Пояснение |
| --- | --- | --- |
| 1 | PASS | Цель и наблюдаемый visual parity outcome заданы. |
| 2 | PASS | AS-IS проверен по обоим XAML styles, tests и blame. |
| 3 | PASS | Root cause — независимые style values и drift radius. |
| 4 | PASS | Design goal — parity без redesign. |
| 5 | PASS | Non-Goals и side-effect/approval границы явные. |
| 6 | PASS | Ответственность трёх файлов и task graph precondition разделены. |
| 7 | PASS | Интеграция task tabs/Roadmap/reference popup описана. |
| 8 | PASS | Reference-based test и minimal style invariant определены. |
| 9 | PASS | Test materialization failure, conflict и rollback заданы. |
| 10 | PASS | Performance неприменим с проверяемой причиной. |
| 11 | PASS | Persistent data не меняются; test UI state восстанавливается. |
| 12 | PASS | API/storage migration отсутствует; theme/Roadmap compatibility учтена. |
| 13 | PASS | Однострочный rollback определён. |
| 14 | PASS | AC1..AC6 измеримы. |
| 15 | PASS | Каждый AC связан с test/diff/evidence; негативные theme/viewport/scope cases есть. |
| 16 | PASS | Exact commands, TDD condition и stop rules заданы. |
| 17 | PASS | Последовательный план и зависимости approval/graph repair заданы. |
| 18 | PASS | Agent-owned решения приняты; user-owned repair сформулирован точным вопросом. |
| 19 | PASS | Short eligibility обоснована риском, не размером текста. |
| 20 | PASS | `dotnet-desktop-client`, `ui-automation-testing` и local override отражены в tests/evidence. |

Итог linter: **ГОТОВО после user-owned combined approval**; FAIL в A/C/D нет, незакрытых HIGH/MEDIUM review findings нет.

### SPEC Rubric

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель и границы | 5 | Один visual outcome, явные Non-Goals и authority boundaries. |
| AS-IS | 5 | Проверены target/reference styles, существующий test и provenance. |
| Конкретность дизайна | 5 | Minimal style change и reference-based regression определены без redesign. |
| Безопасность / миграция / rollback | 5 | Data migration отсутствует; isolated worktree, task graph precondition и rollback явные. |
| Проверяемость | 5 | RED/GREEN, targeted/full suites, themes, viewport и diff gate связаны с AC. |
| Автономность решений | 5 | После combined approval нет инженерного выбора, требующего повторного согласования; stop rules закрывают drift. |

Итого: **30/30 — готово к автономной реализации после exact approval и graph repair permission**. Critical gates имеют приоритет над суммой.

### Post-SPEC Review

- Статус: **ASK-HUMAN** — инженерный контракт готов, но отдельное разрешение на repair task graph блокирует EXEC.
- Scope reviewed: эта SPEC; task `9b8d308b-...`; central instruction stack и local override; planned XAML/test/spec files; current dirty checkout boundary; task graph blockers.
- Evidence inspected:
  - `EmojiFilterMultiSelectSearchBox.axaml` — target chrome и popup structure;
  - `MainControl.axaml`, `GraphControl.axaml` — `FilterOverflowPanel` reference и использования обоих popup;
  - `MainControlFilterToolbarResponsiveUiTests.cs` — existing rendered popup, viewport, interaction and old radius assertion;
  - `global.json`, `dotnet --version`, repo status/branch, `git blame`;
  - authoritative task JSON and parent context;
  - CLI failure evidence: zero-byte task file and missing reverse relation from read-only validation snapshot.
- Review passes:
  - Scope/Evidence pass: PASS — claims trace to inspected files; unrelated feed changes excluded.
  - Contract pass: PASS — visual parity, QUEST, TDD, full suites, UI test and approval requirements covered.
  - Adversarial risk pass: первоначально найдено, что literal `CornerRadius=6` в новом test снова создаст drift; исправлено решением сравнивать computed target с computed reference. Также найден task graph write blocker; вынесен в explicit precondition.
  - Role-Based pass:
    - Business analyst / domain workflow: не применимо — domain workflow/data не меняются; task-status protocol вынесен в precondition.
    - UX / designer: PASS — parity with existing reference, no redesign, visual wireframe and light/dark state.
    - Tester / validation: PASS — expected RED, rendered headless comparison, viewport/interaction/full suites and video fallback.
    - Developer / architect: PASS — minimal local style change, no new abstraction/API, exact rollback.
    - Delivery / operations / security: PASS — isolated worktree, no external delivery, task repo repair separately authorised and Git-recoverable.
  - Fix and re-review: PASS — после добавления reference-based assertion, task graph precondition и video fallback повторно проверены AC, stop rules и scope.
  - Stop decision: ASK-HUMAN с точной combined phrase; EXEC запрещён до получения обеих частей разрешения.
- Depth checklist:
  - Scope drift / unrelated changes: checked; dirty `feat/daily-feed` excluded.
  - Acceptance criteria: AC1..AC6 cover visible chrome and non-regression.
  - User scenarios / Decision ledger / Expected objections: populated above.
  - Validation evidence: commands and expected outcomes specified; EXEC evidence not yet available.
  - Unsupported claims: no claim that current runtime appearance is already fixed; AS-IS limited to inspected source/test values.
  - Regression / edge case: themes, Roadmap, narrow viewport, open/search/toggle flow.
  - Comments/docs/changelog: no production comment/doc impact expected; SPEC audit only.
  - Hidden contract change: automation IDs, behavior, API and persistent data explicitly excluded.
  - Manual-review challenge: likely finding would be a test that compares literals instead of the real reference or a change made in the dirty checkout; both prevented by contract.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | test design | Independent literals would not prevent future drift between popup styles. | Compare computed target and reference chrome. | fixed |
| HIGH | operations | Current task graph is unsafe for CLI writes, so agent cannot claim task per protocol. | Obtain separate permission, perform exact two-path Git-recoverable repair, validate before claim. | ask-human |

- Fixed before continuing: test contract made reference-based; graph blocker made explicit and excluded from implicit SPEC authority.
- Checks rerun: manual linter 1..20, rubric, contract/adversarial/role passes after fixes.
- Needs human: exact combined approval below.
- Residual risks / follow-ups: actual light/dark rendering and full suites await EXEC; task graph repair must be validated before any task mutation.

### Post-EXEC Review

- Статус: **PASS — реализация, обязательные локальные проверки и task completion/read-back завершены**.
- Scope/Evidence pass: PASS — code diff ограничен `EmojiFilterMultiSelectSearchBox.axaml`, `MainControlFilterToolbarResponsiveUiTests.cs` и этой SPEC; automation IDs, ViewModel, storage и popup behavior не менялись.
- Contract pass: PASS — production меняет только `EmojiFilterDropDown.CornerRadius` с `4` на reference `6`; новый rendered headless test сравнивает computed `Background`, `BorderBrush`, `BorderThickness` и `CornerRadius` с реальным `AllTasksFilterPanel` в Light/Dark.
- Adversarial pass: PASS — expected RED был ровно `reference 6` против `emoji 4` в обоих theme cases; после правки тот же test green. Старый самостоятельный literal `CornerRadius(4)` удалён, чтобы не сохранять второй source of truth.
- Role-Based pass:
  - UX / designer: PASS — достигнута согласованность с существующим filter popup без redesign содержимого, размеров или поведения.
  - Tester / validation: PASS — targeted test `2/2`, весь целевой класс `18/18`, full `Unlimotion.Test` `1443/1443` и full `Unlimotion.UiTests.Headless` `49/49` green.
  - Developer / architect: PASS — один style value, reference-based regression, отсутствие новой abstraction/API.
  - Delivery / operations: PASS с отмеченной границей — isolated detached worktree; code push/merge/release не выполнялись. Запущенный Unlimotion Desktop сам создавал backup commits task repo; агент `git push` не вызывал.
- Fix and re-review: PASS — выявленный duplication старого radius assertion удалён; повторный `git diff --check` green, unrelated tracked files отсутствуют.
- Visual evidence fallback: реальное desktop window и screenshot/video не запускались. Доказательство ограничено computed rendered properties в Light/Dark, существующими interaction/viewport tests и полными headless suites; визуальную проверку установленного окна не заявляем.
- Residual risk: возможны platform-specific differences реального compositor/theme rendering, не покрытые headless; изменение не публиковалось и не интегрировалось в основную ветку.
- Stop decision: PASS — результат записан в task, CLI перевёл её в `Completed`; authoritative read-back подтвердил `Completed` и author `/root/task_pull_worker`; финальный graph validate green (`taskCount=2858`, `isValid=true`).

### Reopened EXEC по desktop feedback

- Статус: **NEEDS-FIX** — пользовательский screenshot выявил ложноположительный PASS исходного structural test.
- Evidence: `chat-artifacts/emoji-popup-chrome/task-narrow-alltasks-emoji-open.png`; внешний `Border` имеет radius `6`, но opaque rectangular surface внутреннего `ListBox` доминирует визуально, а светлые border/padding сливаются с фоном окна.
- Scope: остаётся в подтверждённом outcome и тех же target XAML/test/spec files; popup behavior, placement, padding, размеры, bindings и ViewModel не меняются.
- План исправления: TDD RED на opacity фактического `EmojiFilterList` -> прозрачный list background -> targeted/class/full UI validation -> новый desktop screenshot -> повторный post-EXEC review.
- Stop decision: продолжить EXEC по явной команде пользователя «Исправь» как исправление незакрытого visual acceptance criteria; нового product/UX решения не требуется.

### Reopened EXEC — результат и evidence

- TDD RED: новый assertion в `Toolbar_EmojiFilterPopup_UsesFilterFlyoutChrome` упал в Light и Dark с `Expected alpha 0 but received 255`, подтвердив непрозрачный внутренний rectangle как причину screenshot-дефекта.
- Исправление: стиль `ListBox.EmojiFilterList` получил `Background="Transparent"`; outer `EmojiFilterDropDown` остаётся единственным background/border surface. Radius, padding, placement, размеры, bindings и popup logic не менялись.
- Targeted GREEN: `Toolbar_EmojiFilterPopup_UsesFilterFlyoutChrome*` — `2/2`.
- Class regression: `MainControlFilterToolbarResponsiveUiTests/*` — `18/18`.
- Affected solution build: `dotnet build src\Unlimotion.sln -c Debug -p:UseSharedCompilation=false` — exit `0`, errors `0`; остаются существующие Android/package/analyzer warnings.
- Headless UI suite: `Unlimotion.UiTests.Headless` — `49/49`.
- Full unit suite: запускался дважды последовательно. Первый запуск выявил два несвязанных transient failure (`FeedControlUiTests` file lock и live `ServerStorageCrudRealtime`), после чего завис без итоговой сводки; оба теста отдельно прошли (`2/2` и `1/1`). Повторный full run не сообщил новых failures, но снова остался в ожидании после завершения дочернего test host и был остановлен. Поэтому полный suite не заявляется как PASS.
- Desktop evidence boundary: исправленный desktop build успешен, но новый screenshot открытого native popup не получен. FlaUI click завершился Windows `Access denied`; диагностическое автооткрытие не попало в отдельный popup-host кадра. Временный capture/auto-open код полностью удалён и в tracked diff не входит.

### Reopened post-EXEC review

- Scope/Evidence: PASS — permanent diff ограничен XAML, точечным UI regression и этой SPEC; исходная причина доказана RED/GREEN на фактической opacity дочернего surface.
- Contract: PASS — outer chrome по-прежнему совпадает с filter flyout; внутренний list больше не создаёт независимую прямоугольную подложку.
- Adversarial: PASS с границей evidence — поведение popup и viewport regression зелёные, но compositor-level screenshot после исправления отсутствует.
- Roles: пользователь получает заметный rounded chrome без ложной «прозрачной рамки»; tester получает assertion на причину; developer не получает capture hooks; delivery не включает push/merge/release.
- Fix/re-review: исходный structural false positive устранён проверкой inner background alpha; временные диагностические изменения удалены; `git diff --check` повторяется перед завершением.
- Stop decision: PASS для локального исправления и проверенного UI-контракта; residual risk ограничен platform-specific native popup rendering и незавершающимся full-unit runner.

### Повторно открытый EXEC — непрозрачный rounded surface

- User correction: прозрачный `ListBox` отменён; popup должен быть полностью непрозрачным, сохраняя видимое скругление.
- TDD RED: новый контракт упал в Light/Dark на `EmojiFilterDropDown.ClipToBounds == false`. После включения clipping следующий запуск выявил корневую проблему прежнего теста: `ThemeBackgroundBrush` у bare `Popup` резолвился в `null`, как и у внутреннего `FilterOverflowPanel`, поэтому их равенство было ложноположительным.
- Исправление: bare emoji `Popup` теперь воспроизводит фактический chrome стандартного `FlyoutPresenter` через `FlyoutPresenterBackground`, `FlyoutBorderThemeBrush` и `OverlayCornerRadius`; внешний `Border` получил `ClipToBounds=true`, внутренний `ListBox` использует тот же непрозрачный `FlyoutPresenterBackground`.
- Reference regression: тест сравнивает emoji surface с реально отрисованным `FlyoutPresenter`, а также требует alpha `255` у внешнего и внутреннего background.
- Visual evidence (local-only, не коммитится): просмотрены `chat-artifacts/emoji-popup-chrome/20260908-201232/diagnostics-emoji-popup-chrome-window-dark.png` и `...-light.png`; нижележащие строки не просвечивают, фон цельный, четыре угла скруглены.
- Validation:
  - target Light/Dark — `2/2`;
  - `MainControlFilterToolbarResponsiveUiTests` — `18/18`;
  - `dotnet build src\Unlimotion.sln -c Release --no-restore -p:UseSharedCompilation=false --nologo` — exit `0`, errors `0`, existing warnings `31`;
  - первый full Headless — `48/49`, unrelated transient `Daily_note_filename_format_settings`; изолированно `1/1`, повторный full Headless в свежем процессе — `49/49`;
  - full `Unlimotion.Test` serial — `1443/1443` за `21m 04s`.

### Повторный post-EXEC review

- Статус: **PASS**.
- Scope/Evidence pass: проверены утверждённая SPEC, production/test diff, `git status --short`, `git diff --stat`, `git diff --check`, Light/Dark screenshots и все validation outcomes выше; tracked scope ограничен XAML, UI regression и SPEC.
- Contract pass: observable popup непрозрачен, совпадает с фактическим filter flyout chrome и обрезает содержимое по rounded bounds; placement, padding, размеры, bindings, automation IDs и popup behavior не менялись.
- Adversarial risk pass: найдено и исправлено ложное сравнение двух `null` legacy brushes. Временная попытка назвать одинаковый full-window capture отдельным popup-surface evidence удалена; финальный evidence не делает этого неподтверждённого утверждения.
- Role-Based pass:
  - UX / designer: PASS — нет просвечивания, квадратной подложки и визуальной щели; Light/Dark просмотрены;
  - Tester / validation: PASS — RED доказал clipping defect, regression требует opaque alpha и real presenter parity; targeted, class и full suites green;
  - Developer / architect: PASS — используются штатные Fluent flyout resources без hard-coded цветов и новой abstraction;
  - Delivery / operations: PASS — только локальный detached worktree; push/merge/release не выполнялись, screenshots не добавлены в Git.
- Fix and re-review: после удаления misleading surface-capture ветки повторяются targeted test и `git diff --check`; production diff не менялся после полного green validation.
- Stop decision: PASS после повторной точечной проверки; остаточный риск ограничен отсутствием отдельного native-compositor capture, при этом inspected Avalonia rendered frames и полный UI regression green.

### Delivery preparation после rebase на `main`

- Статус: **PASS — ветка изолирована и готова к review**.
- Rebase: исходный checkout находился на detached `HEAD` поверх незамерженной ветки Daily Feed. Создана ветка `fix/emoji-popup-chrome`; только три emoji-коммита перебазированы через `git rebase --onto origin/main dfb0377d`, поэтому Daily Feed не попадает в PR.
- Scope/Evidence pass: `origin/main...HEAD` содержит только эту SPEC, `EmojiFilterMultiSelectSearchBox.axaml` и `MainControlFilterToolbarResponsiveUiTests.cs`; `git rev-list --left-right --count origin/main...HEAD` перед delivery показал `0 3`, rebase прошёл без конфликтов.
- Contract pass: production/test diff после rebase сохраняет утверждённый opaque rounded popup contract; новая база не потребовала изменений кода.
- Validation на актуальном `origin/main`:
  - Light/Dark regression — `2/2`;
  - `MainControlFilterToolbarResponsiveUiTests` — `18/18`;
  - `dotnet build src\Unlimotion.sln -c Release --no-restore -p:UseSharedCompilation=false --nologo` — exit `0`, errors `0`, existing warnings `122`;
  - full `Unlimotion.UiTests.Headless` serial — `38/38` за `2m 15s`;
  - full `Unlimotion.Test` serial — `967/967` за `20m 50s`.
- Adversarial risk pass: отдельно проверена ловушка с базой ветки — обычный rebase перенёс бы десять Daily Feed коммитов; `--onto` оставил только связанный scope. Проверки после rebase выполнены заново, а не перенесены из старой базы.
- Role-Based pass:
  - UX / designer: PASS — ранее просмотренные Light/Dark screenshots подтверждают непрозрачный скруглённый popup; screenshots остаются local-only и не коммитятся;
  - Tester / validation: PASS — targeted, affected class и оба полных набора green на новой базе;
  - Developer / architect: PASS — diff не тянет зависимость от Daily Feed и не меняет API/behavior вне popup chrome;
  - Delivery / operations: PASS — ветка соответствует naming policy, rebase clean, unrelated commits/files исключены; push/PR выполняются только по явной команде пользователя.
- Video fallback: recorder в применённом headless harness отсутствует, отдельного автоматизированного FlaUI сценария emoji popup нет; next-best evidence — deterministic rendered assertions, full Headless suite и local-only Light/Dark screenshots.
- Stop decision: PASS для push ветки и создания ready-for-review PR; merge/release не авторизованы.

## Approval

Получена точная combined phrase из раздела «Риск, objections и открытые решения». Фазовые правила central `quest-mode` соблюдены; расширений scope после approval не было.

## Журнал действий агента

| Фаза / блок | Решение / уверенность | Evidence / что неизвестно | Следующий шаг | Передача человеку / фактическое решение |
| --- | --- | --- | --- | --- |
| SPEC / candidate selection | Выбрана самая узкая agent-executable Prepared задача среди текущих кандидатов; 0.97 | 13 current Prepared: остальные требуют внешних/физических действий либо существенно более широкого code scope | Инспектировать target/reference | Решение агента; user ещё не подтверждал |
| SPEC / AS-IS | Production уже совпадает по background/border/thickness; остаточный drift — radius 4 vs 6; 0.98 | XAML, tests, blame; реальный window screenshot ещё не получен | Сформировать minimal change/test contract | Передача человеку ожидается после review |
| SPEC / graph safety | Task claim невозможен до repair; 1.00 | zero-byte `236a...`; `MissingReverseLink` `62b... -> eadd...` | Запросить отдельное permission | Ожидается решение пользователя |
| SPEC / review | Full post-SPEC review ASK-HUMAN после fixes; 0.97 | Linter 20/20 PASS, rubric 30/30; code contract готов, но graph repair требует отдельного разрешения | Ждать combined approval | Требуется точная фраза пользователя |
| EXEC / approval and graph drift | Combined approval получен; repair сужен по свежему drift; 1.00 | Tombstone tracked+zero-byte удалён; `62b...` и `eadd...` обе стороны пусты, поэтому inverse не создавался; validate green, 2857 tasks | Claim task | Решение пользователя + безопасное сужение агента/root |
| EXEC / claim | Задача закреплена за `/root/task_pull_worker`, RunId `20260908T124655Z-d675ee70`; 1.00 | Description marker и `InProgress` authoritative read-back; validate green | Создать isolated worktree и TDD RED | Выполнено |
| EXEC / TDD | Reference-based regression добавлен до production change; 1.00 | RED: 2/2 theme cases failed only `CornerRadius 4 != 6`; GREEN после minimal XAML change: 2/2 | Проверить affected scope | Выполнено |
| EXEC / validation | Обязательный validation contract green; 0.99 | Целевой класс 18/18; solution build 0 errors/30 existing warnings; full unit 1443/1443; full Headless 49/49 | Post-EXEC review и локальный commit | Выполнено |
| EXEC / visual evidence | Использован предусмотренный SPEC headless fallback; 0.95 | Computed properties Light/Dark и interaction/viewport suites green; real-window screenshot/video не выполнялись | Честно зафиксировать residual risk | Решение агента в пределах SPEC |
| EXEC / review | Full post-EXEC review PASS; 0.98 | Scope/contract/adversarial/role/fix passes; diff-check green; no unrelated tracked changes | Локальный commit, task result, Completed/read-back | Выполнено |
| EXEC / completion | Task formally closed; 1.00 | CLI success; authoritative status/history author read-back; final validate green with 2858 tasks and no issues | Уведомить пользователя через root | Выполнено |
| EXEC / desktop acceptance feedback | Первоначальный PASS отозван; 1.00 | Реальный screenshot показывает квадратный opaque list surface поверх rounded outer border; пользователь явно попросил исправить | Добавить RED на list opacity и выполнить минимальный XAML fix | Решение пользователя получено: «Исправь» |
| EXEC / opaque surface correction | Transparent fix отозван; фактический `FlyoutPresenter` выбран как visual source of truth; 1.00 | RED на missing clipping, затем evidence `ThemeBackgroundBrush == null`; финальные Light/Dark кадры непрозрачны и скруглены | Полные regression suites и review | Решение пользователя: фон не должен быть прозрачным |
| EXEC / final validation and review | Повторный post-EXEC PASS; 0.99 | target 2/2, class 18/18, build 0 errors, Headless 49/49, unit 1443/1443; screenshots inspected; no unrelated tracked files | Локальный commit и отчёт пользователю | Выполнено |
| EXEC / delivery after main rebase | Только три emoji-коммита перенесены на `origin/main`; post-rebase проверки green; 1.00 | `0 3` commits, 3 tracked files; target 2/2, class 18/18, build 0 errors, Headless 38/38, unit 967/967 | Push `fix/emoji-popup-chrome`, создать ready PR и проверить CI | Явная команда пользователя: «Отребейзь на мейн. Оформи PR» |
| EXEC / follow-up rebase | PR #291 повторно перебазирован на `main@91b2d281`; 1.00 | Rebase без конфликтов; `origin/main...HEAD` = `0 3`; target 2/2 и class 18/18 green; production/test diff не изменился | Amend audit, push с exact `--force-with-lease`, проверить PR/CI | Явная команда пользователя от 2026-09-09: «Отребейзь на мейн» |
