# Touch-friendly create menu targets

## 0. Метаданные
- Тип (профиль): `delivery-task`; core `model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `testing-baseline`; context `testing-dotnet`; stack profile `dotnet-desktop-client`; overlay profile `ui-automation-testing`; локальный `AGENTS.override.md`.
- Владелец: Codex / Kibnet.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка.
- Ограничения: фаза SPEC до явной фразы `Спеку подтверждаю`; на EXEC менять только описанные UI/test файлы; сохранить команды, `AutomationId`, локализацию и состав меню.
- Связанные ссылки: `C:\Users\Kibnet\.codex\agents\AGENTS.md`; `C:\Users\Kibnet\.codex\agents\templates\specs\_template.md`; локальный `AGENTS.override.md`.

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Увеличить высоту пунктов меню создания новой карточки, чтобы на телефоне по ним было легче попасть пальцем.

Outcome contract:
- Success means: все пункты глобального меню создания (`NewTask`, `NewSibling`, `NewBlockedSibling`, `NewInner`) имеют touch-friendly hit target не ниже 44 px на телефонной ширине; команды создания продолжают быть теми же; меню остается в пределах экрана.
- Итоговый артефакт / output: XAML-стиль для пунктов меню создания и regression UI test в существующей Avalonia.Headless test suite.
- Stop rules: остановиться после passing targeted UI test, `dotnet build`, полного тестового прогона или после явного отчета о невозможности full-run с next-best evidence; не расширять задачу на редизайн кнопки `+`, карточки задачи или других меню.

## 2. Текущее состояние (AS-IS)
- Глобальная кнопка создания находится в `src/Unlimotion/Views/MainControl.axaml` как `DropDownButton` с `AutomationId="GlobalTaskCreateMenuButton"`.
- Ее `MenuFlyout` содержит четыре `MenuItem`:
  - `GlobalTaskCreateTaskMenuItem` -> `Create`
  - `GlobalTaskCreateSiblingMenuItem` -> `CreateSibling`
  - `GlobalTaskCreateBlockedSiblingMenuItem` -> `CreateBlockedSibling`
  - `GlobalTaskCreateInnerMenuItem` -> `CreateInner`
- Стили кнопки создания уже локализованы в `MainControl.axaml`: `TaskCreateMenuButton`, `TaskAccentOutlineButton`, `TaskIconOnlyDropDownButton`.
- `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` уже проверяет мобильную ширину, видимость кнопки создания и состав меню через `AssertCreateMenuContainsTaskCommands`.
- Текущий тест не фиксирует минимальную высоту пунктов меню, поэтому регрессия hit target не защищена.
- Скрытая зависимость: `AutomationId` используются существующими тестами и должны остаться стабильными.

## 3. Проблема
Пункты меню создания новой карточки визуально и интерактивно слишком низкие для комфортного нажатия пальцем на телефоне, а автоматический контракт на минимальный touch target отсутствует.

## 4. Цели дизайна
- Разделение ответственности: визуальный размер пунктов меню задается в `MainControl.axaml`, тестовая проверка живет в существующем UI layout test class.
- Повторное использование: использовать существующие `AutomationId` и helper-паттерны `MainControlTaskCardLayoutUiTests`.
- Тестируемость: добавить deterministic Avalonia.Headless assertion на высоту пунктов меню на телефонной ширине.
- Консистентность: менять только create menu, не затрагивая date quick selection, context menus и action menu.
- Обратная совместимость: сохранить команды, заголовки через `DynamicResource`, порядок пунктов и accessibility names/automation ids.

## 5. Non-Goals (чего НЕ делаем)
- Не менять поведение создания задач, relations, deadline reset или keyboard shortcuts.
- Не менять размер самой кнопки `GlobalTaskCreateMenuButton`.
- Не редизайнить карточку задачи, toolbar, tabs, filter flyouts или action menu.
- Не менять строки локализации.
- Не добавлять новый UI framework, новые зависимости или screenshot/video инфраструктуру.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> добавить локальный стиль/класс для пунктов меню создания и применить его к четырем `MenuItem`.
- `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` -> расширить phone-width layout coverage проверкой, что пункты create menu имеют минимальную высоту и остаются доступны через текущие `AutomationId`.

### 6.2 Детальный дизайн
- Данные/команды: не меняются; `MenuItem.Command` остается привязан к текущим `Create*` commands.
- Контракты / API: публичные API и view-model контракты не меняются.
- Output contract / evidence rules: evidence после EXEC включает targeted UI test, build и full test run или объективно объясненный fallback.
- Visual planning artifact для UI-facing изменений:

```text
Телефонная ширина, глобальное меню создания:

До:  [+] -> [ New task            ]  низкий ряд
            [ New sibling         ]  низкий ряд
            [ New blocked sibling ]  низкий ряд
            [ New inner           ]  низкий ряд

После: [+] -> [ New task            ]  >= 44 px hit target
             [ New sibling         ]  >= 44 px hit target
             [ New blocked sibling ]  >= 44 px hit target
             [ New inner           ]  >= 44 px hit target

Структура, порядок, заголовки и команды не меняются.
```

- UI test video evidence для UI automation задач: `Не применимо` для обязательного видео, потому что релевантный локальный suite здесь Avalonia.Headless/TUnit и текущий harness не сохраняет безопасные video artifacts. Fallback evidence: automated layout assertions по bounds/hit target, test output и при необходимости screenshot/log on failure.
- Границы сохранения поведения: `MenuFlyout` остается `MenuFlyout`; другие `MenuItem` в `MainControl.axaml` не получают новый стиль.
- Обработка ошибок: не требуется, изменение layout-only.
- Производительность: impact отсутствует; увеличение min height четырех menu items не добавляет вычислений и не меняет data flow.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Минимальная высота каждого пункта меню создания должна быть `>= 44` px после показа flyout на телефонной ширине.
- Все четыре ожидаемых пункта должны присутствовать и иметь ненулевые bounds после layout pass.
- Порядок пунктов остается: new task, new sibling, new blocked sibling, new inner.

## 8. Точки интеграции и триггеры
- Триггер UI: пользователь нажимает `GlobalTaskCreateMenuButton`.
- Триггер теста: headless test создает `MainControl` на ширинах `360/390/430`, показывает flyout и проверяет `MenuItem.Bounds.Height`.
- Пересчет layout выполняется стандартным Avalonia measure/arrange + `Dispatcher.UIThread.RunJobs()`.

## 9. Изменения модели данных / состояния
- Новые поля: нет.
- Persisted vs calculated: нет изменений persisted state.
- Влияние на хранилище: нет.

## 10. Миграция / Rollout / Rollback
- Первый запуск: без миграции.
- Обратная совместимость: существующие команды, ресурсы и `AutomationId` сохраняются.
- Rollback: удалить стиль/класс у create menu items и удалить/вернуть связанный тестовый assertion.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - AC1: четыре пункта `GlobalTaskCreate*MenuItem` имеют `Bounds.Height >= 44` после открытия flyout на phone-width layout.
  - AC2: меню содержит те же четыре команды с теми же `AutomationId`.
  - AC3: кнопка создания остается видимой и горизонтально помещается в телефонной ширине.
  - AC4: существующие сценарии создания задач через меню/команды не меняются.
- Какие тесты добавить/изменить:
  - Обновить `MainControlTaskCardLayoutUiTests`, предпочтительно в `CurrentTaskCard_PhoneWidthLayout_DoesNotOverflowAndKeepsRelationEditorUsable`, либо добавить отдельный targeted test `CurrentTaskCreateMenu_PhoneWidth_UsesTouchFriendlyMenuItems`.
  - Тест должен открыть `MenuFlyout` через `ShowAt(createMenuButton)`, выполнить layout jobs, найти `MenuItem` по `AutomationId` и проверить высоту.
- Characterization tests / contract checks для текущего поведения: сначала добавить/запустить новый failing UI assertion до XAML-исправления, затем исправить стиль.
- Visual acceptance для UI-facing изменений: текстовый wireframe выше; automated assertion проверяет ключевую визуальную метрику `>= 44` px и сохранение пунктов.
- UI video evidence для UI-facing фич/багфиксов: baseline/post video `Не применимо`, потому что текущий Avalonia.Headless/TUnit harness не пишет video. Next-best evidence: targeted test output, bounds assertion и full build/test evidence.
- Базовые замеры до/после для performance tradeoff: `Не применимо`, layout-only изменение четырех пунктов меню.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/CurrentTaskCreateMenu_PhoneWidth_UsesTouchFriendlyMenuItems"`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`
- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted test не компилируется из-за test runner syntax, выполнить `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --list-tests` и поправить `--treenode-filter`.
  - Если full test run зависает или упирается в environment/cache issue, сохранить точный вывод, выполнить targeted UI suite/class и `dotnet build`, затем явно сообщить fallback.

## 12. Риски и edge cases
- Риск: глобальный стиль `MenuItem` заденет другие меню. Смягчение: использовать отдельный класс или селектор, примененный только к create menu items.
- Риск: высокий пункт меню увеличит flyout и он выйдет за компактный viewport. Смягчение: тестировать phone width; всего четыре пункта по 44 px остаются компактным меню.
- Риск: text clipping на длинной локализации. Смягчение: не задавать фиксированную ширину, увеличивать vertical hit target через min height/padding.
- Риск: Avalonia `MenuFlyout` detached content сложнее инспектировать. Смягчение: использовать существующий паттерн `flyout.ShowAt(button)` из UI тестов и `menuFlyout.Items.OfType<MenuItem>()`.

## 13. План выполнения
1. EXEC после `Спеку подтверждаю`: добавить/обновить UI test, который падает на текущей высоте пунктов меню.
2. Добавить локальный стиль/класс в `MainControl.axaml` и применить к четырем create menu items.
3. Запустить targeted UI test.
4. Запустить `dotnet build src/Unlimotion.sln`.
5. Запустить `dotnet test src/Unlimotion.sln` или зафиксировать объективный fallback.
6. Выполнить post-EXEC review-loop и исправить найденные проблемы.

## 14. Открытые вопросы
Нет блокирующих вопросов. Предлагаемый порог `>= 44` px соответствует типовой touch target и достаточно близок к текущему размеру кнопки создания `42` px.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`.
- Выполненные требования профиля:
  - UI change покрывается существующим Avalonia.Headless UI suite.
  - Стабильные `AutomationId` сохраняются и используются в тесте.
  - Запланированы targeted UI test, `dotnet build` и full test run.
  - Video evidence имеет явный fallback с причиной: текущий headless harness не пишет video artifacts.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Добавить стиль/класс для touch-friendly create menu items и применить к четырем пунктам меню | Увеличить hit target без изменения поведения |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Добавить assertion/test на `MenuItem.Bounds.Height >= 44` на phone width | Зафиксировать UI regression coverage |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Create menu item hit target | Не зафиксирован; пункты визуально ниже желаемого finger target | Минимум 44 px для каждого пункта |
| Commands / order | `Create`, `CreateSibling`, `CreateBlockedSibling`, `CreateInner` | Без изменений |
| UI tests | Проверяется состав меню, но не размер пунктов | Проверяется состав и touch-friendly высота |
| Video evidence | Не применимо | Не применимо; fallback automated UI assertions |

## 18. Альтернативы и компромиссы
- Вариант: поднять глобальный стиль `MenuItem`.
- Плюсы: меньше XAML на конкретном меню.
- Минусы: меняет все меню приложения, выше риск layout regressions.
- Почему выбранное решение лучше в контексте этой задачи: задача просит только кнопки/пункты создания новой карточки, поэтому локальный стиль с существующими `AutomationId` минимизирует blast radius.

- Вариант: увеличить только padding без `MinHeight`.
- Плюсы: проще визуально.
- Минусы: тестовый контракт менее прямой, итоговая высота может зависеть от темы/шрифта.
- Почему выбранное решение лучше в контексте этой задачи: `MinHeight >= 44` прямо выражает finger target.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Поверхность изменения ограничена XAML + UI test; данные/миграция не меняются. |
| C. Безопасность изменений | 11-13 | PASS | Acceptance, rollback и edge cases зафиксированы; blast radius локальный. |
| D. Проверяемость | 14-16 | PASS | Есть targeted UI assertion, build и full test команды. |
| E. Готовность к автономной реализации | 17-19 | PASS | План по EXEC конкретный, открытых блокеров нет. |
| F. Соответствие профилю | 20 | PASS | UI automation и .NET desktop требования отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Задача сведена к высоте четырех пунктов create menu. |
| 2. Понимание текущего состояния | 5 | Указаны XAML controls, commands, automation ids и текущий test class. |
| 3. Конкретность целевого дизайна | 5 | Задан `MinHeight >= 44`, локальный стиль и test strategy. |
| 4. Безопасность (миграция, откат) | 5 | Нет data migration, rollback прост, scope ограничен. |
| 5. Тестируемость | 5 | Есть failing-first UI assertion, targeted command и full validation. |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов; план конкретный. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-create-menu-touch-targets.md`, instruction stack (`model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, local `AGENTS.override.md`), open questions, planned changed files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: просмотрены `MainControl.axaml` участки стилей и create menu, `MainControlTaskCardLayoutUiTests.cs` участки desktop/phone layout и create menu assertion, `global.json`, `Directory.Packages.props`, `Unlimotion.Test.csproj`.
  - Contract pass: spec ограничивает изменение create menu, сохраняет commands/localization/automation ids, планирует UI test и full .NET checks.
  - Adversarial risk pass: проверены риски глобального `MenuItem` style leakage, viewport growth, localization clipping и detached flyout inspection.
  - Re-review after fixes / Fix and re-review: исправления после review не потребовались.
  - Stop decision: PASS, можно запрашивать `Спеку подтверждаю`.
- Evidence inspected: `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `src/Directory.Packages.props`, `global.json`, local `AGENTS.override.md`, центральные QUEST/testing/profile docs.
- Depth checklist:
  - Scope drift / unrelated changes: planned files ограничены XAML + test; рабочее дерево перед SPEC было чистым.
  - Acceptance criteria: AC1-AC4 измеримы.
  - Validation evidence: команды проверки перечислены; video fallback обоснован.
  - Unsupported claims: claim про 44 px оформлен как целевой UI threshold, а не как внешняя policy-ссылка.
  - Regression / edge case: проверены leakage на другие меню, clipping и phone viewport.
  - Comments/docs/changelog: changelog не нужен для small UI fix; комментарии не планируются.
  - Hidden contract change: commands/order/automation ids сохраняются.
  - Manual-review challenge: пользовательское ревью скорее всего спросит, не затронет ли стиль другие меню и есть ли тест на телефонную ширину; оба пункта закрыты.
- No-findings justification: spec имеет конкретный target, ограниченный blast radius, measurable UI acceptance и релевантную UI test strategy.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence не будет создано из-за отсутствия recorder в Avalonia.Headless harness | Использовать fallback: automated bounds assertions + test output | accepted-risk |

- Fixed before continuing: не требовалось.
- Checks rerun: SPEC linter/rubric self-check выполнены вручную по central docs.
- Needs human: требуется явное подтверждение `Спеку подтверждаю` для перехода в EXEC.
- Residual risks / follow-ups: full test run может быть долгим; если он зависнет, нужен зафиксированный fallback.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, `git diff --stat`, relevant diff for `MainControl.axaml` and `MainControlTaskCardLayoutUiTests.cs`, validation output from targeted UI tests, project build, full project test run, full solution build blocker.
- Decision: можно завершать; full solution build/test заменены next-best checks из-за отсутствующего workload `wasm-tools` для Android/iOS проектов.
- Review passes:
  - Scope/Evidence pass: inspected changed XAML style/class application, new headless UI test and helper, spec journal, `git diff --check`, `git status --short`, `git diff --stat`.
  - Contract pass: create menu commands, order, `DynamicResource` headers and `AutomationId` values are preserved; only four global create menu items receive the new class.
  - Adversarial risk pass: checked style leakage risk, unsupported Avalonia property regression, detached `MenuFlyout` bounds measurement and phone-width assertions.
  - Re-review after fixes / Fix and re-review: after `VerticalContentAlignment` caused AVLN2000, removed the unsupported setter and reran targeted UI test successfully.
  - Stop decision: PASS with explicit environment fallback for full solution validation.
- Evidence inspected:
  - Pre-fix targeted UI test failed as expected: `GlobalTaskCreateTaskMenuItem` height was 27 px for widths 360/390/430.
  - Post-fix targeted UI test passed: `3` total, `3` passed, `0` failed.
  - `dotnet build src/Unlimotion.sln` failed before project compilation because SDK requires `wasm-tools` for `Unlimotion.Android` and `Unlimotion.iOS`.
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj` passed.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build` passed: `530` total, `530` passed, `0` failed.
  - `git diff --check` passed; only line-ending conversion warnings were reported for touched files.
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated tracked files; current changes are the two planned project files plus this approved spec.
  - Acceptance criteria: AC1 covered by new bounds assertion; AC2 covered by existing `AssertCreateMenuContainsTaskCommands`; AC3 covered by phone-width test; AC4 covered by preserving command bindings and full `Unlimotion.Test` run.
  - Validation evidence: targeted UI fail/pass, affected project build, full affected test project run and whitespace check are recorded.
  - Unsupported claims: no external factual claims; `44` px is the target contract from spec.
  - Regression / edge case: style scoped to `MenuItem.TaskCreateMenuItem`; no global `MenuItem` style added.
  - Comments/docs/changelog: no code comments needed; changelog not required for small UI fix.
  - Hidden contract change: commands, resources, order and automation ids unchanged.
  - Manual-review challenge: likely review concern would be whether other menus changed or whether this is tested at phone width; both are addressed by local class and headless UI test.
- No-findings justification: implementation matches the approved spec, has a failing-before/passing-after UI assertion, and validation evidence covers the affected Avalonia surface.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full solution build/test could not run because Android/iOS projects require missing SDK workload `wasm-tools` | Use affected project build plus full `Unlimotion.Test` run as next-best evidence and report blocker | accepted-risk |

- Fixed before final report: removed unsupported `VerticalContentAlignment` setter from `MenuItem.TaskCreateMenuItem`.
- Checks rerun: targeted UI test, affected project build, full affected test project, `git diff --check`.
- Validation evidence: listed above.
- Unrelated changes: none detected in tracked files; spec is new and part of approved QUEST flow.
- Needs human: no.
- Residual risks / follow-ups: full solution validation requires installing/restoring `wasm-tools` workloads for Android/iOS projects.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | instruction-stack-and-context | 0.95 | Нет | Создать рабочую spec | Нет | Нет | Собран QUEST/UI/.NET stack, подтвержден SPEC gate | `AGENTS.override.md`, central instruction docs |
| SPEC | code-surface-discovery | 0.90 | Нет | Зафиксировать planned files и тесты | Нет | Нет | Найдены create menu, automation ids и релевантный UI test class | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| SPEC | spec-authoring-and-review | 0.92 | Подтверждение пользователя | Запросить `Спеку подтверждаю` | Да | Да, запрос подтверждения спеки | Spec готова к EXEC, но QUEST запрещает кодовые изменения без явного подтверждения | `specs/2026-06-16-create-menu-touch-targets.md` |
| EXEC | approval-and-failing-test | 0.90 | Targeted test output | Запустить targeted UI test до XAML-фикса | Нет | Да, пользователь подтвердил `Спеку подтверждаю` | Перешел в EXEC и добавил regression assertion на `MenuItem.Bounds.Height >= 44` без изменения XAML | `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, `specs/2026-06-16-create-menu-touch-targets.md` |
| EXEC | pre-fix-validation | 0.95 | Нет | Исправить XAML стиль пунктов меню | Нет | Нет | Targeted UI test упал на высоте `GlobalTaskCreateTaskMenuItem` 27 px для ширин 360/390/430, что подтвердило дефект | `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | xaml-fix | 0.90 | Passing targeted test output | Запустить targeted UI test после фикса | Нет | Нет | Добавлен локальный `MenuItem.TaskCreateMenuItem` с `MinHeight=44` и применен только к create menu items | `src/Unlimotion/Views/MainControl.axaml`, `specs/2026-06-16-create-menu-touch-targets.md` |
| EXEC | xaml-compile-fix | 0.90 | Passing targeted test output | Повторить targeted UI test | Нет | Нет | Убран неподдерживаемый `VerticalContentAlignment` у `MenuItem`; оставлены `MinHeight` и `Padding` | `src/Unlimotion/Views/MainControl.axaml`, `specs/2026-06-16-create-menu-touch-targets.md` |
| EXEC | validation | 0.88 | Нет | Выполнить post-EXEC review | Нет | Нет | Targeted UI test прошел 3/3; solution build заблокирован `wasm-tools`; affected project build прошел; full `Unlimotion.Test` прошел 530/530 | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | post-exec-review | 0.92 | Нет | Завершить отчет | Нет | Нет | Review подтвердил соответствие spec, отсутствие global style leakage и достаточный fallback для environment blocker | `specs/2026-06-16-create-menu-touch-targets.md` |
