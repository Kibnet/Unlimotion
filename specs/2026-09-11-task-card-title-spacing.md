# Выровнять заголовок и строку «Важное» в карточке задачи

## Цель и границы

- Metadata:
  - task: `2eb07b54-4631-44f4-86a6-f0a20eb43bee`;
  - owner: `Codex agent /root`;
  - branch: `fix/task-card-title-spacing`;
  - worktree: `C:\Users\Kibnet\.codex\worktrees\task-2eb07b54-task-card-title-spacing`;
  - форма: Short — один локальный обратимый UI outcome, без данных, API, конфигурации, миграции и внешнего side effect;
  - профиль: `dotnet-desktop-client` + `ui-automation-testing`;
  - context: `testing-dotnet` + `visual-feedback` + targeted `session-insights-context`;
  - instruction stack: central `creator-vibe-lens`, `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `tool-execution-baseline`, SPEC linter/rubric/review loops и локальный `AGENTS.override.md`;
  - effective runtime: локальный Codex на Windows/PowerShell; exact model/tier не влияет на XAML-контракт и не используется как validation evidence.
- AS-IS:
  - `CurrentTaskTitleTextBox` находится в `src/Unlimotion/Views/MainControl.axaml` и использует классы `CurrentTaskTitleEditor BorderlessTextBoxChrome`;
  - `BorderlessTextBoxChrome` убирает рамку и фон, но не меняет `TextBox.Padding`;
  - `CurrentTaskTitleEditor` задаёт только `MinHeight`, `FontWeight` и вертикальное выравнивание;
  - поэтому заголовок наследует внутренний padding стандартного `TextBox` поверх уже заданного расстояния до `TaskStatusPicker` (`Grid.ColumnSpacing` и правый `Margin` статуса);
  - существующий `MainControlTaskCardLayoutUiTests` проверяет borderless chrome, но не защищает отсутствие внутреннего отступа у заголовка.
- Проблема: визуально перед текстом заголовка остаётся лишнее пустое место, хотя поле должно восприниматься как обычный заголовок карточки, а не как содержимое стандартного input.
- Уточнение после первой визуальной приёмки: `Padding=0` сам по себе не является результатом. На снятом UI начало заголовка всё ещё заметно правее начала слова «Важное».
- Наблюдаемый outcome задаётся двумя вертикальными направляющими:
  - левый край переключателя статуса совпадает с левым краем квадратного индикатора checkbox «Важное»;
  - начало видимого содержимого заголовка совпадает с началом слова «Важное» как для заголовка с начальным emoji, так и для обычного текстового заголовка без emoji.
- Non-Goals:
  - не сдвигать сам `TaskStatusPicker` и checkbox «Важное» относительно левого края карточки;
  - не убирать общий padding карточки;
  - не менять padding описания, критериев выполнения и других `TextBox`;
  - не менять binding, wrapping, placeholder, focus, automation id, ViewModel, данные или storage;
  - не менять mobile/desktop структуру карточки и не делать общий редизайн;
  - не создавать commit, push или PR без отдельной просьбы пользователя.

## Результат, решения и проверки

### Visual planning artifact

Лёгкая текстовая схема достаточна для локального изменения одного spacing token; отдельный графический mockup был бы непропорционален.

```text
AS-IS:
│ [статус]         Заголовок
│ [checkbox]  Важное
                 ^ заголовок заметно правее текста второй строки

TO-BE:
│ [статус]    Заголовок
│ [checkbox]  Важное
│             ^ одна направляющая для текстового содержимого
^ одна направляющая для двух индикаторов
```

### Decision Ledger и Acceptance-to-Test Matrix

| Observable scenario / решение (owner) | AC / ожидаемый результат | Команда / evidence |
| --- | --- | --- |
| Пользователь открывает карточку на desktop; owner решения — пользователь через approval этой SPEC | X начала status picker совпадает с X индикатора «Важное»; X начала rendered title content совпадает с X текста «Важное» с допуском 1 DIP | geometry assertion в `MainControlTaskCardLayoutUiTests.CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls` |
| Пользователь открывает карточку на ширине 360/390/430; owner решения — пользователь через approval этой SPEC | обе направляющие сохраняются, карточка не получает overflow | parameterized `CurrentTaskCard_PhoneWidthLayout_DoesNotOverflowAndKeepsRelationEditorUsable` + те же geometry assertions |
| Заголовок начинается с emoji | первый видимый glyph emoji расположен на направляющей слова «Важное»; собственный bearing emoji не маскирует лишний layout inset | screenshot `root-description` на desktop и phone + visual review |
| Заголовок не содержит emoji | первая буква заголовка расположена на той же направляющей слова «Важное» | screenshot `repeater-planning` на desktop и phone + visual review |
| Заголовок получает focus/hover | рамка и фон остаются borderless, geometry не меняется | существующий `AssertBorderlessTextBoxChrome` и targeted UI class |
| Редактирование заголовка | binding `Title`, placeholder, wrapping, focus и `CurrentTaskTitleTextBox` сохраняются | relevant diff + существующие task-card/headless UI tests |
| Визуальная приёмка | wide и narrow screenshots показывают исчезнувший внутренний inset без ухудшения плотности и обрезания | README media UX-review harness; local-only artifacts, ручная инспекция изображений |

### Решение, revision 2

- Предыдущее решение `CurrentTaskTitleEditor.Padding=0` сохранить как полезное устранение input inset, но не считать достаточным acceptance evidence.
- В `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` заменить формальную проверку padding на проверку фактических X-координат content presenters заголовка и checkbox «Важное», а также левых краёв status/checkbox indicators. Автоматическая geometry-проверка отвечает за layout origins; отдельные screenshots с emoji и без него отвечают за glyph-level визуальную приёмку.
- В `src/Unlimotion/Views/MainControl.axaml` убрать лишний межколоночный интервал только внутри `CurrentTaskHeader`: expected-red measurement показал title content `X=34` против wanted label `X=28`, при уже совпадающих status/checkbox indicators. Поэтому минимальное изменение — только `TaskCardHeader.ColumnSpacing=0` для normal и compact styles; status margin не меняется.
- Если после этого content presenters расходятся более чем на 1 DIP, не компенсировать расхождение отрицательным margin. Вместо этого остановиться и уточнить структуру template/layout отдельной поправкой к SPEC.
- Не менять общий `TaskStatusPicker` style или общий `BorderlessTextBoxChrome`: tree rows и другие поля имеют собственные spacing-контракты.
- Сохранить существующие selectors/classes и automation id.
- Изменяемые implementation-файлы после approval:
  - `src/Unlimotion/Views/MainControl.axaml`;
  - `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`.
- Данные/состояние, совместимость и миграция: не применимо — persisted model и runtime state не меняются.
- Ошибки/recovery: новых error states нет. При regression targeted UI test блокирует завершение.
- Performance: не применимо — один локальный XAML setter не создаёт измеримого runtime tradeoff.
- Rollback: удалить локальный setter и соответствующее regression assertion либо revert будущего implementation change set.

### Expected User Review Objections

- «Свойство стало нулевым, но глазами всё ещё криво» — acceptance основан на координатах rendered content и before/after screenshots, а не на `TextBox.Padding`.
- «Сдвинулся переключатель статуса» — его X сравнивается с X индикатора checkbox «Важное» до и после.
- «На телефоне стало тесно или текст обрезался» — compact widths 360/390/430 входят в UI test и visual evidence.
- «Заодно сломали другие поля» — общий `BorderlessTextBoxChrome` не меняется; diff ограничен title-specific style.
- «Тест проверяет реализацию, а не результат» — geometry property assertion дополняется wide/narrow layout tests и визуальной инспекцией.

### План и stop rules

1. После повторного exact approval `Спеку подтверждаю` добавить geometry assertions и получить ожидаемый TDD red на текущем визуальном смещении.
2. Обнулить только `TaskCardHeader.ColumnSpacing` в normal и compact style, сохранив X status picker и checkbox indicator.
3. Выполнить targeted test, весь `MainControlTaskCardLayoutUiTests` и affected desktop build последовательно; полный non-UI suite не входит в acceptance по последнему решению пользователя.
4. Сгенерировать wide/narrow screenshots штатным UX-review harness и визуально проверить обе направляющие, обрезание, переносы и плотность отдельно на `root-description` с emoji и `repeater-planning` без emoji.
5. Выполнить `git diff --check`, post-EXEC review и User-Observable Completion Gate.
6. После успешного результата дополнить описание задачи evidence/путями и перевести её в `Completed` штатным CLI; код оставить uncommitted/unpushed без отдельной просьбы.

Проверочные команды:

```powershell
dotnet restore src\Unlimotion.Test\Unlimotion.Test.csproj
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls" --maximum-parallel-tests 1 --no-ansi --no-progress
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --maximum-parallel-tests 1 --no-ansi --no-progress
dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --no-restore -p:UseSharedCompilation=false /nodeReuse:false
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --no-ansi --no-progress
dotnet run --project tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -c Debug -- --ux-review task-card --language ru --output-root artifacts\chat-artifacts\task-card-title-spacing-current
git diff --check -- src\Unlimotion\Views\MainControl.axaml src\Unlimotion.Test\MainControlTaskCardLayoutUiTests.cs specs\2026-09-11-task-card-title-spacing.md
```

- Если первый regression test не фиксирует расхождение X-координат, остановить EXEC и исправить способ измерения вместо speculative XAML.
- Если status picker или checkbox indicator меняет левую X-координату, исправление отклоняется.
- Если обнуление локальных spacing tokens не выравнивает rendered content в пределах 1 DIP, остановиться без отрицательных margins и вернуть решение в SPEC.
- Если targeted UI class красный, не запускать full suite до исправления.
- После timeout не повторять идентичную команду без progress/root-cause evidence и новой гипотезы.
- Падающий обязательный UI test блокирует завершение. Полный non-UI suite исключён из текущего acceptance по явному решению пользователя от 2026-09-12 как непропорциональный локальному XAML spacing change; этот выбор остаётся отклонением от общего central full-suite gate и должен быть честно отражён в финальном отчёте.
- Screenshot/video: обязательное видео не применимо — статическое изменение spacing не образует многошаговый flow, а harness даёт deterministic UI assertions и screenshots. Fallback evidence: targeted UI tests + wide/narrow screenshots.

## Quality gate и review

### SPEC Linter 1..20

| № | Статус | Пояснение |
| --- | --- | --- |
| 1 | PASS | Цель сформулирована как наблюдаемое исчезновение внутреннего inset. |
| 2 | PASS | AS-IS подтверждён `MainControl.axaml` и текущим test contract. |
| 3 | PASS | Корень локализован в отсутствующем title-specific override `TextBox.Padding`. |
| 4 | PASS | Дизайн сохраняет межкомпонентный интервал и убирает только внутренний. |
| 5 | PASS | Non-Goals исключают общий redesign и соседние controls. |
| 6 | PASS | XAML отвечает за style, UI test — за regression contract. |
| 7 | PASS | Указаны style resolution, automation selector и screenshot harness. |
| 8 | PASS | Инвариант zero-padding локален `CurrentTaskTitleEditor`. |
| 9 | PASS | Новых error states нет; test failure блокирует completion. |
| 10 | PASS | Performance неприменим с проверяемой причиной. |
| 11 | PASS | Persisted данные и состояние не меняются. |
| 12 | PASS | API/binding/automation compatibility явно сохранена; миграция не нужна. |
| 13 | PASS | Rollback сводится к удалению локального setter/test change. |
| 14 | PASS | AC задают exact padding и пользовательские wide/narrow outcomes. |
| 15 | PASS | Каждый observable scenario связан с UI/evidence; задан негативный TDD red. |
| 16 | PASS | Команды и stop rules приведены. |
| 17 | PASS | План сохраняет TDD и staged validation. |
| 18 | PASS | Решение о типе отступа явно принадлежит approval пользователя; блокирующих вопросов нет. |
| 19 | PASS | Short обоснован локальным обратимым scope. |
| 20 | PASS | Учтены desktop, UI automation и visual-feedback требования. |

Итог linter: `ГОТОВО`, FAIL в A/C/D нет.

### SPEC Rubric

| Критерий | Балл | Обоснование |
| --- | --- | --- |
| Цель и границы | 5 | Один observable outcome и узкие Non-Goals. |
| AS-IS | 5 | Проверены конкретные style selectors, XAML structure и существующий UI test. |
| Конкретность дизайна | 5 | Назван один setter, его selector и намеренно неизменяемые интервалы. |
| Безопасность/миграция/rollback | 5 | Данных/API нет; rollback локален и обратим. |
| Проверяемость | 5 | TDD red, targeted/class/full tests, build и screenshots связаны с AC. |
| Автономность решений | 5 | После approval нет открытого implementation choice. |

Итого: `30/30`, готово к автономной реализации после exact approval.

### Post-SPEC Review

- Статус: `PASS`.
- Scope reviewed: эта SPEC; central instruction stack; local `AGENTS.override.md`; `MainControl.axaml` styles/header; `MainControlTaskCardLayoutUiTests`; предыдущая borderless-title SPEC; задача и отдельный worktree.
- Decision: можно запрашивать подтверждение SPEC.
- Review passes:
  - Scope/Evidence pass: planned change ограничен title-specific XAML setter и regression UI assertions; implementation diff отсутствует.
  - Contract pass: visual planning artifact, scenarios, decision ledger, AC→test, objections, rollback и stop rules заполнены.
  - Adversarial risk pass: проверены альтернативные источники — card padding, grid spacing и status margin; они намеренные и исключены из scope. Риск «не тот отступ» снят явным approval decision.
  - Role-Based pass: применимые UX, tester, developer и delivery роли рассмотрены ниже.
  - Fix and re-review: initial draft дополнен явным сохранением status-to-title spacing и TDD stop rule; повторная сверка linter/rubric — PASS.
  - Stop decision: `PASS`.
- Role-Based Review Result:
  - Business analyst / domain workflow: не применимо — бизнес-правила и task state приложения не меняются.
  - UX / designer: PASS — убирается двойной inset, сохраняется читаемое разделение controls, wide/narrow входят в acceptance.
  - Tester / validation: PASS — предусмотрены expected red, targeted/class/full TUnit и visual evidence.
  - Developer / architect: PASS — общий style не меняется, API/binding/selectors сохранены.
  - Delivery / operations / security: PASS — отдельный worktree; commit/push/PR исключены; security/config отсутствуют.
- Evidence inspected:
  - `src/Unlimotion/Views/MainControl.axaml`, строки стилей `CurrentTaskCard`, `TaskCardHeader`, `CurrentTaskTitleEditor`, `BorderlessTextBoxChrome` и header markup;
  - `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, desktop и phone-width scenarios;
  - `specs/2026-06-16-task-card-title-borderless-editor.md`, сохранённый borderless contract и screenshot workflow;
  - task metadata/status через актуальный `unlimotion-cli`.
- Depth checklist:
  - scope drift / unrelated changes: implementation отсутствует; только текущая рабочая SPEC;
  - acceptance criteria: exact zero-padding + visual geometry;
  - scenarios / ledger / objections: заполнены;
  - validation evidence: команды определены, выполнение относится к EXEC;
  - unsupported claims: computed pixel value не заявляется до TDD measurement;
  - regression / edge case: focus/hover, desktop и compact widths включены;
  - comments/docs/changelog: не требуются; spec является trace artifact;
  - hidden contract change: status spacing, binding и automation остаются прежними;
  - manual-review challenge: вероятный вопрос — не следовало ли убрать margin статуса; ответ зафиксирован в decision ledger и визуальной схеме.
- No-findings justification: после уточнения типа отступа не осталось незакрытых HIGH/MEDIUM; scope локален и каждый риск связан с проверкой.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | UX scope | «Отступ перед тайтлом» мог означать grid/status spacing, а не внутренний padding | Явно выбрать internal padding и передать решение пользователю через approval SPEC | fixed |

- Fixed before continuing: добавлены visual before/after и explicit Non-Goal для status/grid spacing.
- Checks rerun: linter 1..20, rubric 6 критериев, contract/adversarial passes.
- Needs human: exact `Спеку подтверждаю`.
- Residual risks / follow-ups: фактическое computed padding и expected TDD red подтверждаются только на EXEC; если red не воспроизводится, действует stop rule.

### Post-EXEC Review

- Статус revision 1: `REJECTED BY USER VISUAL REVIEW` — zero padding не обеспечил требуемую направляющую.
- Статус revision 2: `PASS` в явно согласованном пользователем UI-only validation scope.
- Scope reviewed: approved revision 2 этой SPEC; `MainControl.axaml`; `MainControlTaskCardLayoutUiTests.cs`; `git status --short`; relevant diff; task-card UX-review screenshots с emoji и без него на desktop/phone.
- Decision: implementation outcome достигнут; task остаётся формально `Completed`, код остаётся uncommitted/unpushed.
- Review passes:
  - Scope/Evidence pass: revision 2 production diff — `TaskCardHeader.ColumnSpacing=0` в normal/compact styles; status margin и card padding не менялись. Test diff заменяет implementation-detail padding assertion на geometry contract.
  - Contract pass: expected red зафиксировал title/wanted `X=34.0/28.0`; после fix targeted UI test прошёл 1/1 и весь `MainControlTaskCardLayoutUiTests` — 20/20.
  - Adversarial risk pass: geometry contract отдельно проверяет status↔checkbox indicator и title content↔wanted label; phone tests покрывают 360/390/430. Screenshots охватывают emoji и обычный текст, поэтому emoji bearing не маскирует result.
  - Role-Based pass: UX — PASS на wide/narrow и двух вариантах заголовка; tester — PASS в согласованном UI-only scope; developer — PASS по локальности XAML/test diff; delivery — PASS, публикации нет.
  - Fix and re-review: первоначальная revision 1 была отклонена по visual review; revision 2 измеряет actual layout origin, повторно выполнены targeted/class UI tests и screenshot harness.
  - Stop decision: `PASS`.
- Role-Based Review Result:
  - Business analyst / domain workflow: не применимо — task state, storage и commands не меняются.
  - UX / designer: PASS — две визуальные направляющие подтверждены на desktop/phone, с emoji и без него.
  - Tester / validation: PASS — expected red, targeted/class headless UI tests и deterministic screenshot harness выполнены.
  - Developer / architect: PASS — нет изменения template, binding, selector или global style; change ограничен task-card header.
  - Delivery / operations / security: PASS — no commit/push/PR/release, binary artifacts local-only.
- Evidence inspected:
  - UI test: `MainControlTaskCardLayoutUiTests`, 20/20 за 55.4 с;
  - expected red: `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls`, 0/1 с `X=34.0/28.0`;
  - targeted green: тот же test, 1/1;
  - screenshot report: `C:\Users\Kibnet\AppData\Local\Temp\unlimotion-task-card-header-alignment-20260912-010553\report.json`, 11 assets, `Warnings: []`;
  - inspected screenshots: `desktop/root-description`, `desktop/repeater-planning`, `phone/root-description-card`, `phone/repeater-planning-card`;
  - `git diff --check`: clean; warnings LF/CRLF являются Git working-copy warnings, не diff errors.
- Depth checklist:
  - scope drift / unrelated changes: только header styles, relevant UI assertions и current SPEC;
  - acceptance criteria: обе pairs of guides проверены geometry + screenshots;
  - validation evidence: targeted/class UI green и screenshots; full suite намеренно не входит в user-approved scope;
  - unsupported claims: build/full-suite green этой revision не заявляется;
  - regression / edge case: compact widths 360/390/430, emoji/no-emoji и unchanged status X;
  - comments/docs/changelog: code comments/changelog не нужны; SPEC обновлена;
  - hidden contract change: bindings, automation ids, input focus/template и task data не изменены;
  - manual-review challenge: проверить, что status не сдвинулся и обычный текст без emoji тоже не уехал — закрыто двумя assertion pairs и четырьмя screenshots.
- No-findings justification: после visual rejection revision 1 текущий diff напрямую устраняет измеренный 6-DIP gap и покрывает все обозначенные пользователем variants.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | UX evidence | Revision 1 проверял property, а не фактическую направляющую | Заменить assertion на geometry contract и повторить visual review | fixed |
| LOW | UI evidence | Automated runner/harness не поддерживает запись видео для статического layout change | Fallback: deterministic headless UI assertions + before/after screenshots | accepted-risk |
| LOW | Validation scope | Full `Unlimotion.Test` не запущен после revision 2 | Следовать прямому user decision «только ui и скриншоты»; не заявлять full green | accepted-risk |

- Fixed before final report: `ColumnSpacing=0`, geometry regression contract, rerun targeted/class UI and screenshot harness.
- Checks rerun: targeted desktop 1/1; task-card UI class 20/20; diff check; screenshot report/visual inspection.
- Validation evidence: UI-only по прямому решению пользователя; recorder unavailable in current harness, therefore screenshots are the objective fallback.
- Unrelated changes: нет; working tree содержит только текущие XAML/test/spec files.
- Needs human: нет.
- Residual risks / follow-ups: UI suite покрывает layout origins; actual glyph rasterization остаётся визуально проверена только на supplied deterministic Russian fixtures.

## Approval

Первоначальный approval относится к revision 1 и не разрешает новую геометрию. Для revision 2 ожидается повторная точная фраза `Спеку подтверждаю`. Фазовые правила — central `quest-mode`.

## Журнал действий агента

| Фаза / блок | Решение / уверенность | Evidence / что неизвестно | Следующий шаг | Передача человеку / фактическое решение |
| --- | --- | --- | --- | --- |
| SPEC / task claim | Задача закреплена за `/root`, status `InProgress`; 1.0 | `unlimotion-cli set-status` и read-back | Создать отдельный worktree | Пользователь поручил выполнить в отдельном worktree |
| SPEC / worktree | `fix/task-card-title-spacing` от `origin/main@3aa24c8f`; 1.0 | `git worktree add`, clean branch | Локализовать inset | Решение не требовалось |
| SPEC / inspection | Источник принят как inherited `TextBox.Padding`, а не межкомпонентный spacing; 0.9 | XAML styles/header и существующие UI tests; computed value ещё не измерен | Подготовить TDD assertion | Требуется approval выбранной интерпретации |
| SPEC / quality gate | Short SPEC, linter PASS, rubric 30/30, post-SPEC PASS; 0.95 | Review evidence выше | Ожидать approval | Ожидается точная фраза `Спеку подтверждаю` |
| EXEC / approval | SPEC подтверждена; 1.0 | Пользователь: `Спеку подтверждаю` | Добавить regression assertion и получить expected red | Фактическое решение пользователя получено |
| EXEC / TDD setup | Zero-padding assertion добавлен в desktop и phone-width scenarios; 0.9 | Implementation XAML ещё не менялся | Запустить targeted desktop test | Решение не требуется |
| EXEC / expected red | Regression test упал на фактическом `Padding=10,6,6,5`; 1.0 | `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls`: 0/1, ожидаемый defect | Добавить локальный `Padding=0` | Решение не требуется |
| EXEC / implementation | В `CurrentTaskTitleEditor` добавлен единственный setter `Padding=0`; 0.98 | Общие стили, status margin и grid spacing не менялись | Повторить targeted test | Решение не требуется |
| EXEC / targeted green | Тот же desktop regression test прошёл 1/1; 1.0 | `Padding=0` удовлетворяет неизменённому assertion | Запустить весь task-card UI class | Решение не требуется |
| EXEC / affected UI class | `MainControlTaskCardLayoutUiTests` прошёл 20/20 за 55.8 с; 1.0 | Desktop и phone-width scenarios зелёные | Собрать desktop и запустить full suite | Решение не требуется |
| EXEC / affected build | `Unlimotion.Desktop` собран успешно, 0 ошибок; 1.0 | Только два LF/CRLF warning для изменённых файлов | Запущен full suite | Решение не требуется |
| EXEC / validation scope change | Full suite остановлен по явному решению пользователя; 1.0 | Пользователь: «для такой фичи все тесты прогонять бессмысленно, только ui и скриншоты посмотри»; run завершён exit 1 после Ctrl+C, product failure не зафиксирован | Выполнить screenshot harness и visual self-review | Фактическое решение пользователя получено; central full-suite gate остаётся незакрытым |
| EXEC / screenshot harness | Изменённый worktree снят штатным task-card UX-review harness; 1.0 | 11 assets, `Warnings: []`, desktop и phone viewports | Получить baseline тем же harness | Решение не требуется |
| EXEC / baseline comparison | Чистый `origin/main@3aa24c8f` снят теми же fixtures; 1.0 | Before/after desktop root, phone card и desktop repeater просмотрены в original resolution | Провести post-EXEC review | Решение не требуется |
| EXEC / visual review | Удалён только внутренний title inset; внешний промежуток, wrapping и плотность карточки сохранены; 0.98 | Before/after screenshots, UI tests 20/20, affected build green | Зафиксировать результат в задаче | Решение не требуется |
| EXEC / post-review | PASS в явно согласованном UI-only validation scope; 0.98 | Diff локален, `git diff --check` без ошибок, visual evidence без warnings | Завершить task через CLI | Full suite сознательно не заявляется как пройденный |
| EXEC / task completion | Результат и evidence записаны в описание, status `Completed`; 1.0 | Read-back задачи после `unlimotion-cli complete` | Передать пользователю итог и пути к скриншотам | Код остаётся uncommitted/unpushed согласно scope |
| SPEC revision 2 / user feedback | Результат revision 1 визуально отклонён: заголовок остаётся правее «Важное»; 1.0 | Прямой feedback пользователя после просмотра screenshot | Уточнить две вертикальные направляющие | Решение пользователя требуется |
| SPEC revision 2 / alignment target | Status picker ↔ checkbox indicator и title content ↔ слово «Важное»; 1.0 | Пользователь явно подтвердил обе пары | Обновить AC, тест и XAML strategy | Фактическое решение пользователя получено |
| SPEC revision 2 / title variants | Визуальная приёмка должна покрыть заголовки с emoji и без emoji; 1.0 | Прямое уточнение пользователя | Добавить оба screenshot fixture в AC | Фактическое решение пользователя получено |
| SPEC revision 2 / gate | Поправка готова; 0.95 | Observable geometry, stop rules и UI-only validation определены | Ожидать повторный exact approval | Ожидается `Спеку подтверждаю` |
| EXEC revision 2 / TDD red | Geometry assertion воспроизвёл defect: title content `X=34.0`, wanted label `X=28.0`; 1.0 | targeted desktop UI test 0/1; status/checkbox assertion прошёл | Обнулить только header column spacing | Решение не требуется |
| EXEC revision 2 / minimal fix | `TaskCardHeader.ColumnSpacing=0` применён в normal и compact style; 0.98 | Status margin не менялся; production diff остаётся header-local | Повторить targeted UI test | Решение не требуется |
| EXEC revision 2 / targeted green | Тот же scenario прошёл 1/1; 1.0 | title/wanted и status/checkbox guides совпали | Запустить весь task-card UI class | Решение не требуется |
| EXEC revision 2 / UI class | `MainControlTaskCardLayoutUiTests` прошёл 20/20 за 55.4 с; 1.0 | desktop и phone 360/390/430 зелёные | Снять emoji/no-emoji screenshots | Решение не требуется |
| EXEC revision 2 / visual review | `root-description` с emoji и `repeater-planning` без emoji просмотрены на desktop/phone; 1.0 | 11 assets, `Warnings: []`, обе направляющие визуально совпадают | Post-EXEC review | Решение не требуется |
| EXEC revision 2 / post-review | PASS в согласованном UI-only scope; 0.98 | geometry contract, UI class, screenshots и diff check | Передать пользователю screenshots | Full suite не заявляется как пройденный |
