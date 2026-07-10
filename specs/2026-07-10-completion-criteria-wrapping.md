# Перенос длинных критериев выполнения в карточке задачи

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex / Unlimotion
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до утверждения спеки код и тесты не менять; сохранить существующие automation-id; не менять модель данных и semantics критериев выполнения
- Связанные ссылки: Не применимо, внешней issue-ссылки нет

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
В карточке текущей задачи сделать длинный текст критерия выполнения переносимым по строкам, как уже переносится title. Сейчас длинный criterion в `CompletionCriterionTextBox` визуально обрезается справа внутри доступной ширины, особенно на узком экране.

Outcome contract:
- Success means: длинный критерий выполнения с пробелами переносится внутри строки критерия, полностью остаётся видимым в карточке и не создаёт горизонтального overflow на phone-width layout.
- Итоговый артефакт / output: точечная XAML-правка `CompletionCriterionTextBox`, regression UI test в существующем Avalonia.Headless suite, validation evidence.
- Stop rules: остановиться после passing targeted UI test, build и full/next-best test evidence; если full run заблокирован локальной средой, зафиксировать точную причину и targeted evidence.

## 2. Текущее состояние (AS-IS)
- UI карточки текущей задачи живёт в `src/Unlimotion/Views/MainControl.axaml`.
- Title в `CurrentTaskTitleTextBox` уже имеет `TextWrapping="Wrap"` и поэтому длинный заголовок переносится в карточке.
- Критерии выполнения рендерятся через `DataTemplate DataType="domain:TaskCompletionCriterion"` в `Grid ColumnDefinitions="Auto,*,Auto"`: checkbox, `TextBox` с `AutomationId="CompletionCriterionTextBox"`, remove button.
- Стиль `TextBox.CompletionCriterionTextBox` задаёт compact chrome (`MinHeight`, `Padding`, `VerticalContentAlignment`), но не задаёт `TextWrapping`.
- Существующая regression surface: `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, включая phone-width checks и helpers `AssertNoHorizontalOverflow`, `AssertHorizontallyContained`.

## 3. Проблема
Одна корневая проблема: `CompletionCriterionTextBox` остаётся single-line/no-wrap, поэтому длинный criterion не раскрывает правую часть текста в текущей карточке и визуально теряется за пределами видимой области текста.

## 4. Цели дизайна
- Разделение ответственности: layout behavior фиксируется в XAML view, без изменений ViewModel/domain.
- Повторное использование: применить тот же принцип, что у title: `TextWrapping="Wrap"` на editor control.
- Тестируемость: добавить targeted UI regression test с длинным criterion на phone width.
- Консистентность: сохранить текущую borderless compact строку, checkbox/remove button и automation-id.
- Обратная совместимость: не менять persisted data, commands, status semantics, disabled editing behavior.

## 5. Non-Goals (чего НЕ делаем)
- Не менять модель `TaskCompletionCriterion`, persistence, sync или status-transition rules.
- Не менять текстовые ресурсы, локализацию, порядок секций карточки или command behavior.
- Не внедрять новый custom control для criteria.
- Не менять layout relation/repeater/planning sections вне возможного indirect reflow от высоты длинного criterion.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> включает wrapping для `CompletionCriterionTextBox`.
- `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` -> добавляет regression coverage для длинного criterion в phone-width карточке.
- `specs/2026-07-10-completion-criteria-wrapping.md` -> хранит решение, validation plan и журналы QUEST.

### 6.2 Детальный дизайн
- Потоки данных: не меняются; `Text="{Binding Text}"` остаётся прежним.
- Контракты / API: не меняются.
- Output contract / evidence rules: показать passing targeted UI test и build/full-run или объективный fallback.
- Visual planning artifact для UI-facing изменений:

```text
Phone-width task card, completion criteria section

Before:
[ ]  Очень длинный критерий выполнения, который продолжается вправо ... [x]
     правая часть текста недоступна внутри single-line editor

After:
[ ]  Очень длинный критерий выполнения, который              [x]
     продолжается на следующей строке и остаётся видимым
     внутри доступной ширины карточки
```

- UI test video evidence для UI automation задач: fallback. В существующем `src/Unlimotion.Test` Avalonia.Headless/TUnit workflow нет настроенного video recorder/artifact hook; next-best evidence: failing/passing headless assertion по wrapping, высоте editor и отсутствию horizontal overflow. Если после EXEC будет быстро доступен безопасный screenshot/render artifact, приложить его как local-only visual evidence.
- Границы сохранения поведения: checkbox слева и remove button справа остаются в той же строке; только text editor получает multi-line wrapping и может увеличить высоту строки.
- Обработка ошибок: не применимо, ошибок runtime/API не добавляется.
- Производительность: negligible; wrapping применяется только к видимым text editors в текущей карточке.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Любой criterion text с пробелами должен переноситься внутри доступной ширины text column.
- Checkbox и remove button должны оставаться полностью видимыми и кликабельными.
- Disabled completed-task criteria editing behavior остаётся прежним.

## 8. Точки интеграции и триггеры
- Новая логика применяется при layout/render каждого `TaskCompletionCriterion` в `ItemsControl ItemsSource="{Binding CompletionCriteria}"`.
- Пересчёт layout выполняет Avalonia при изменении текста, ширины окна или текущей задачи.

## 9. Изменения модели данных / состояния
- Новые поля: нет.
- Persisted vs calculated: persisted text не меняется; меняется только визуальное измерение/перенос.
- Влияние на хранилище: нет.

## 10. Миграция / Rollout / Rollback
- Поведение при первом запуске: существующие criteria отображаются с wrapping без миграции.
- Обратная совместимость: сохранена.
- План отката: вернуть XAML-свойство wrapping/связанный style change и удалить regression test.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. `CompletionCriterionTextBox` получает wrapping, сопоставимый с `CurrentTaskTitleTextBox`.
  2. На ширине 360/390 px длинный criterion с пробелами увеличивает высоту editor/row и не скрывает правую часть single-line clipping.
  3. Completion criteria section не создаёт horizontal overflow внутри `CurrentTaskDetailsScrollViewer`.
  4. Checkbox и remove button остаются видимыми, contained и с прежними automation-id.
  5. Существующие tests про borderless compact editing, disabled editing и focus нового criterion не ломаются.
- Какие тесты добавить/изменить:
  - Добавить в `MainControlTaskCardLayoutUiTests` test наподобие `CurrentTaskCard_LongCompletionCriterion_PhoneWidthWrapsWithoutHorizontalOverflow`.
  - Test setup: открыть phone-width current task, добавить длинный `TaskCompletionCriterion.Text`, найти `CompletionCriterionTextBox`, проверить `TextWrapping == Wrap`, `Bounds.Height > 28`, `AssertNoHorizontalOverflow(scrollViewer, card)`, contained checkbox/remove.
- Characterization tests / contract checks для текущего поведения: новый test сначала должен воспроизвести дефект до XAML-правки через height/wrapping assertion.
- Visual acceptance для UI-facing изменений: результат должен соответствовать wireframe above: текст уходит на 2+ строки внутри card; checkbox/remove остаются видимыми.
- UI video evidence для UI-facing фич/багфиксов: fallback по причине отсутствия video recorder в текущем headless suite; evidence = targeted UI test output + optional local-only screenshot/render if feasible.
- Базовые замеры до/после для performance tradeoff: Не применимо, изменение layout-only.
- Команды для проверки:
  - Фактический TUnit runner в этом checkout: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --maximum-parallel-tests 1 --output Detailed`
  - `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj --no-restore /nodeReuse:false`
  - Full/next-best: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --maximum-parallel-tests 1 --output Detailed`
- Stop rules для test/retrieval/tool/validation loops: targeted UI test должен пройти; full test failures считать blocker только если они связаны с изменёнными files/contract, иначе изолировать и зафиксировать residual risk.

## 12. Риски и edge cases
- Длинная строка без пробелов может всё ещё overflow при обычном word wrapping; это не входит в текущую жалобу, но test text должен содержать реальные слова.
- Рост высоты criterion row может сдвинуть нижние секции; это ожидаемо, вертикальный scroll уже есть.
- Если Avalonia TextBox не auto-sizes enough только от `TextWrapping`, может потребоваться дополнительный XAML tweak без изменения ViewModel.

## 13. План выполнения
1. Добавить reproducing UI test для phone-width long criterion.
2. Запустить targeted test и убедиться, что до фикса он падает по wrapping/height.
3. Включить wrapping у `CompletionCriterionTextBox` в XAML.
4. Повторить targeted UI test, затем build и full/next-best test.
5. Выполнить post-EXEC review, проверить diff scope и validation evidence.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`; context `testing-dotnet`; core `quest-governance`, `collaboration-baseline`, `testing-baseline`.
- Выполненные требования профиля:
  - UI behavior covered by existing Avalonia.Headless UI suite.
  - Stable automation-id сохраняются.
  - План включает targeted UI test, build, full/next-best test evidence.
  - Visual planning artifact включён; video fallback обоснован.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Добавить wrapping для `CompletionCriterionTextBox` | Сделать длинный criterion полностью видимым |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Добавить phone-width regression test с длинным criterion | Зафиксировать баг и предотвратить возврат single-line clipping |
| `specs/2026-07-10-completion-criteria-wrapping.md` | Вести QUEST spec и журнал | Central governance |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Completion criterion text | Single-line/no-wrap, правая часть текста скрывается | Wrap внутри доступной ширины, полный текст доступен вертикально |
| Title editor | Wrap | Без изменений |
| Criteria controls | Checkbox/text/remove | Без изменений состава и selectors |
| Данные | Без изменений | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: добавить `TextWrapping="Wrap"` прямо на `TextBox`.
- Плюсы: минимально, повторяет title pattern, не меняет общий style контракт.
- Минусы: XAML property локален для template.
- Почему выбранное решение лучше в контексте этой задачи: проблема находится в одном editor instance, а title уже задаёт wrapping на control usage.

- Вариант: добавить `TextWrapping` в style `TextBox.CompletionCriterionTextBox`.
- Плюсы: централизует behavior для класса.
- Минусы: если класс переиспользуют где-то ещё, может изменить шире ожидаемого.
- Почему не выбран как первый вариант: локальная property точнее соответствует текущему DataTemplate.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, integration, данные, rollback и perf описаны. |
| C. Безопасность изменений | 11-13 | PASS | Scope малый, модель данных не меняется, план и риски есть. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, UI test plan и команды проверки указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Блокирующих вопросов нет, альтернативы и file table есть. |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation requirements отражены, video fallback обоснован. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Корневая проблема и Non-Goals узкие. |
| 2. Понимание текущего состояния | 5 | Указаны XAML template/style и существующий UI test suite. |
| 3. Конкретность целевого дизайна | 5 | Решение сводится к wrapping на editor и проверяемому phone-width behavior. |
| 4. Безопасность (миграция, откат) | 5 | Data/API не меняются, rollback простой. |
| 5. Тестируемость | 5 | Есть reproducing UI test, targeted/build/full commands и visual acceptance. |
| 6. Готовность к автономной реализации | 5 | Блокеров нет, план и критерии достаточно конкретны. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-07-10-completion-criteria-wrapping.md`; instruction stack `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, local `AGENTS.override.md`; selected profile `dotnet-desktop-client` + `ui-automation-testing`; open questions none; planned changed files `MainControl.axaml`, `MainControlTaskCardLayoutUiTests.cs`, current spec.
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: проверены central/local instructions, `MainControl.axaml` styles/template, `MainControlTaskCardLayoutUiTests.cs` phone-width helpers, current git status clean before spec.
  - Contract pass: spec covers UI test update, stable automation-id, visual artifact, video fallback, no data/API changes.
  - Adversarial risk pass: long unbroken token scoped out; row-height reflow accepted because vertical scroll exists; full-run risk handled by next-best evidence rule.
  - Re-review after fixes / Fix and re-review: первичный draft уже содержит required visual artifact, fallback и commands; дополнительных правок по review не потребовалось.
  - Stop decision: no BLOCKER/HIGH findings; ready for human approval.
- Evidence inspected: `src/Unlimotion/Views/MainControl.axaml` lines around title/criteria template; `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` phone-width tests/helpers; central QUEST/testing docs; local AGENTS override.
- Depth checklist:
  - Scope drift / unrelated changes: spec-only mutation before approval; planned code scope two files.
  - Acceptance criteria: measurable via wrapping property, height, no overflow, contained controls.
  - Validation evidence: commands specified; execution deferred to EXEC.
  - Unsupported claims: video fallback tied to lack of configured recorder in current headless workflow.
  - Regression / edge case: long words called out as residual/non-goal; existing criteria behavior preserved.
  - Comments/docs/changelog: no comments/docs/changelog expected beyond spec.
  - Hidden contract change: no API/data/selector changes planned.
  - Manual-review challenge: reviewer would likely ask whether current no-overflow helper catches internal TextBox clipping; spec answers with explicit wrapping/height assertion.
- No-findings justification: small layout-only change, clear existing title pattern, direct regression test planned, no blocking open questions.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence unavailable in current headless workflow. | Use explicit fallback: targeted UI test output and optional screenshot if feasible. | accepted-risk |

- Fixed before continuing: Не применимо.
- Checks rerun: SPEC linter/rubric self-check only; code validation deferred to EXEC.
- Needs human: требуется фраза `Спеку подтверждаю`.
- Residual risks / follow-ups: long unbroken tokens may need separate hard-wrap policy if user reports that case.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec `specs/2026-07-10-completion-criteria-wrapping.md`; `git status --short`; `git diff --stat`; relevant diff in `src/Unlimotion/Views/MainControl.axaml` and `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`; validation evidence from red targeted test, green targeted/class/full test runs, desktop build and `git diff --check`.
- Decision: можно завершать
- Review passes:
  - Scope/Evidence pass: diff scope matches spec: one XAML property, one regression UI test, spec updates; no unrelated tracked changes.
  - Contract pass: acceptance criteria covered: wrapping property, phone-width row height, contained checkbox/text/remove controls, no horizontal overflow, existing layout class green.
  - Adversarial risk pass: checked that the fix does not alter binding, commands, automation-id, data model or adjacent criteria controls; full suite passed despite known post-summary unobserved cleanup exception output.
  - Re-review after fixes / Fix and re-review: after XAML fix, reran targeted test, whole affected layout class, desktop build and full `Unlimotion.Test`.
  - Stop decision: PASS; no blocker/high findings remain.
- Evidence inspected:
  - Red test: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/CurrentTaskCard_LongCompletionCriterion_PhoneWidthWrapsWithoutHorizontalOverflow" --maximum-parallel-tests 1 --output Detailed` failed before fix with `Expected Wrap, received NoWrap`.
  - Green targeted test: same command passed 1/1 after XAML fix.
  - Affected layout class: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --maximum-parallel-tests 1 --output Detailed` passed 20/20.
  - Desktop build: `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj --no-restore /nodeReuse:false` passed with 0 errors and existing warnings.
  - Full test project: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --maximum-parallel-tests 1 --output Detailed` passed 556/556 in 12m 59s, exit code 0; runner printed a post-summary unobserved timeout exception.
  - Whitespace: `git diff --check` passed with line-ending warnings only.
- Depth checklist:
  - Scope drift / unrelated changes: `git status --short` shows only planned files and new spec.
  - Acceptance criteria: all five criteria checked by new test, existing class and build/full suite.
  - Validation evidence: concrete command outputs inspected; initial `dotnet test` route did not find test projects, so TUnit `dotnet run` was used.
  - Unsupported claims: no visual/video claim made beyond automated assertions; video fallback stated explicitly.
  - Regression / edge case: long unbroken tokens remain out of scope; normal long word-spaced criterion covered.
  - Comments/docs/changelog: no code comments or changelog needed for this UI bugfix.
  - Hidden contract change: binding, selectors, commands, storage and status behavior unchanged.
  - Manual-review challenge: likely concern is whether row height growth breaks phone layout; whole task-card layout class and full suite passed.
- No-findings justification: implementation is a one-property view fix with direct red/green UI regression coverage and broad affected-suite validation.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | No automated video artifact was produced. | Use fallback evidence because `src/Unlimotion.Test` Avalonia.Headless/TUnit workflow has no configured video recorder; rely on red/green UI assertions and TUnit HTML report. | accepted-risk |
| LOW | validation | Full run printed a post-summary unobserved timeout exception despite exit code 0 and 556/556 passed. | Report as residual local test-run cleanup noise; no product regression evidence from assertions. | accepted-risk |

- Fixed before final report: XAML wrapping fix added after red test.
- Checks rerun: targeted test, affected layout class, desktop build, full `Unlimotion.Test`, `git diff --check`.
- Validation evidence: listed above.
- Unrelated changes: none detected.
- Needs human: none.
- Residual risks / follow-ups: long unbroken tokens may require a separate hard-wrap policy if reported later; no video artifact generated for this headless run.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction stack и repo scan | 0.84 | Нет утверждения спеки для EXEC | Создать spec и запросить подтверждение | Да | Нет | Central QUEST требует spec-first; AS-IS найден в `MainControl.axaml` и existing UI suite. | `AGENTS.md`, `AGENTS.override.md`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| SPEC | SPEC quality gate | 0.88 | Нет утверждения спеки для EXEC | Запросить фразу `Спеку подтверждаю` | Да | Нет | Linter/rubric/review прошли; кодовые изменения отложены до approval. | `specs/2026-07-10-completion-criteria-wrapping.md` |
| EXEC | Approval received | 0.9 | Нет | Добавить reproducing UI test до XAML-фикса | Нет | Да: пользователь написал `Спеку подтверждаю` | Единственный разрешённый переход SPEC->EXEC выполнен, можно менять код в границах спеки. | `specs/2026-07-10-completion-criteria-wrapping.md` |
| EXEC | Reproducing UI test | 0.9 | Нет | Включить wrapping в XAML и повторить targeted test | Нет | Нет | Новый TUnit/Avalonia.Headless test падает до фикса на `Expected Wrap, received NoWrap`, что воспроизводит дефект. | `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | XAML fix and targeted green | 0.9 | Нужно проверить весь affected layout class | Запустить `MainControlTaskCardLayoutUiTests` целиком | Нет | Нет | `CompletionCriterionTextBox` получил `TextWrapping=Wrap`; новый targeted test проходит 1/1 и проверяет no horizontal overflow. | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | Validation and post-EXEC review | 0.92 | Нет | Финальный sanity-pass и отчёт пользователю | Нет | Нет | Affected layout class прошёл 20/20, desktop build прошёл, полный `Unlimotion.Test` прошёл 556/556; post-EXEC review PASS с video fallback. | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `specs/2026-07-10-completion-criteria-wrapping.md` |
