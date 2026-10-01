# Открытие задач по внешним ссылкам `unlimotion://task/{id}`

## Контекст

- Owner: Codex; ветка `feat/daily-feed`, исходный HEAD `3b8246a0`; в рабочем дереве уже находятся незафиксированные изменения предыдущей подтверждённой SPEC, их нельзя смешивать или откатывать.
- Форма: **Expanded**, потому что меняется публичный OS-протокол, packaging lifecycle и межпроцессная активация. Профили: `dotnet-desktop-client`, `ui-automation-testing`; контекст проверки: `testing-dotnet`.
- Поручение: при щелчке в Obsidian по ссылке вида `unlimotion://task/feed-12422d3acca249db950bccce95f0d723` Windows должна передать ссылку Unlimotion, а приложение — открыть соответствующую задачу.
- Проверенный AS-IS: Live Preview внутри Unlimotion уже распознаёт префикс `unlimotion://task/` и через `FeedViewModel.OpenTaskReference` открывает задачу активного пространства. `Unlimotion.Desktop.Program` принимает только `--config`, системная URL-схема не регистрируется, single-instance/activation broker отсутствует. Windows release собирается Velopack; установленное приложение имеет стабильный launcher вне обновляемого каталога `current`.
- Platform scope: первая реализация — установленное desktop-приложение Windows, поскольку подтверждённый пользовательский сценарий выполняется на Windows/Obsidian. Объявление схемы в macOS `Info.plist`, Linux `.desktop` и mobile app links остаётся отдельным расширением.

## Пять содержательных проверок

| Проверка | Решение / ожидаемый результат | План проверки → фактическое evidence |
| --- | --- | --- |
| 1. Результат / границы | Windows регистрирует per-user URL scheme `unlimotion` с командой `"<stable Unlimotion launcher>" "%1"`. Поддерживается только канонический маршрут `unlimotion://task/{taskId}` с одним непустым безопасным ID. При холодном запуске URI сохраняется до завершения `MainWindowViewModel.Connect`, после чего приложение включает режим «Задачи», выбирает задачу, открывает её карточку и выводит окно на передний план. | Добавить изолированный parser/activation request в desktop-слой, coordinator между startup и `App`, единый метод навигации к задаче в `MainWindowViewModel`/`App`, локализованные ошибки и Windows protocol registrar. Не менять формат Markdown-ссылок: уже созданные ссылки начинают работать без миграции. |
| 2. Решения / разрешения | Если Unlimotion уже запущен, процесс, стартовавший по URI, передаёт запрос первому desktop-экземпляру через bounded named-pipe activation broker и завершается; обычный запуск без URI не запрещает несколько экземпляров. Если broker недоступен, новый процесс продолжает холодный запуск, поэтому ссылка не теряется. Разрешение выполняется только в активном пространстве задач: автоматическое переключение/перебор других пространств без source-id запрещено; при отсутствии задачи показывается «Задача {id} не найдена в текущем пространстве». | Это сохраняет нынешнюю модель нескольких пространств и не делает скрытых сетевых подключений/переключений. Для будущего точного межпространственного перехода потребуется расширенный URI с идентификатором пространства, но канонический старый URI останется совместимым. |
| 3. AC→evidence | AC1: parser принимает пример пользователя и отклоняет другой scheme/host, пустой ID, дополнительные path segments и опасные/невалидные ID. AC2: cold start выполняет навигацию только после готовности task repository. AC3: warm activation передаётся ровно один раз, восстанавливает/активирует главное окно и открывает задачу. AC4: отсутствующая задача не меняет текущую выбранную задачу и показывает локализованную ошибку. AC5: install/update регистрируют стабильную команду; uninstall удаляет только принадлежащую Unlimotion регистрацию. AC6: существующее открытие task-link внутри Ленты не регрессирует. | TDD: parser/coordinator/registry abstraction unit tests; Avalonia Headless UI test на успешную и отсутствующую задачу; AppAutomation/FlaUI desktop activation test с тестовым URI и уже запущенным окном; packaging contract tests для Velopack hooks. Реальную HKCU-регистрацию в unit/UI tests не менять: registry adapter тестируется через изолированную реализацию, а установленный smoke выполняется отдельно при сборке installer. |
| 4. Существенный риск / rollback | Риски: command injection через URI, ссылка теряется во время startup, registry указывает на versioned exe после update, uninstall удаляет чужую регистрацию, IPC принимает произвольные команды. Поэтому parser allowlist-ит только `task/{id}`, IPC передаёт структурированный task-id с ограничением длины, registry path берётся из стабильного Velopack root launcher, а delete сверяет владельца/команду. | Rollback: убрать Velopack hooks/registrar и activation broker; данные задач и заметок не меняются. Уже сохранённые Markdown URI останутся обычным текстом и снова смогут заработать после возврата поддержки. |
| 5. Findings / disposition / stop | Корень отсутствующего поведения — не рендеринг ссылок, а внешний desktop activation pipeline. Рекомендуемый UX не создаёт новую вкладку или отдельное окно: открывается существующая карточка задачи в основном окне. Obsidian/Windows могут показать собственное подтверждение открытия внешнего приложения — Unlimotion этим диалогом не управляет. | Post-SPEC: **PASS** — маршрут, cold/warm activation, startup ordering, security boundary, task-space limitation, packaging lifecycle и тестовые уровни определены. Остановиться до точного «Спеку подтверждаю»; runtime, registry и packaging до approval не менять. |

## Архитектурный поток

```text
Obsidian
  → Windows URL protocol (`unlimotion`)
  → stable Velopack launcher + URI argument
  → TaskDeepLinkParser
      ├─ running instance found → named pipe → UI dispatcher
      └─ no receiver → normal cold startup → pending activation
  → task repository ready
  → resolve task in active task space
  → Tasks mode + selected task card + restored foreground window
```

## User-Observable Scenarios

1. При закрытом Unlimotion щелчок по `unlimotion://task/{id}` запускает приложение, ждёт загрузки активного пространства и открывает карточку задачи.
2. При уже запущенном Unlimotion новый URI-процесс передаёт ссылку первому экземпляру, тот восстанавливает окно и открывает карточку ровно один раз.
3. Если задача отсутствует в активном пространстве, текущий выбор не меняется, пользователь видит локализованную ошибку.
4. Неканонический или опасный URI не выполняет команд и не открывает задачу.
5. Внутренние ссылки Ленты продолжают открывать ту же карточку задачи.

Визуальный planning artifact: **не применимо** — новая поверхность не создаётся; итоговое состояние полностью задано существующими «Задачи» → выбранная задача → открытая карточка. Наблюдаемое состояние проверяется Avalonia Headless.

## Decision Ledger

| Решение | Причина | Отвергнутая альтернатива |
| --- | --- | --- |
| Per-user Windows URL protocol через Velopack lifecycle | Не требует elevation и переживает update благодаря стабильному root launcher | Регистрировать versioned `current` exe — ломается после update |
| Named pipe только для URI-запуска | Сохраняет разрешённые обычные multi-instance запуски | Делать всё приложение single-instance — меняет несвязанный контракт |
| Разрешение только в активном task space | URI не содержит source-id, скрытый перебор/переключение неоднозначны | Автоматически искать по всем пространствам |
| Строгий allowlist ID и один маршрут `task/{id}` | IPC и shell получают минимальный безопасный контракт | Передавать произвольный URI/команду |
| Семантический Headless вместо видео | Визуальный layout не меняется, проверяется навигационное состояние | Записывать видео без нового визуального поведения |

## Acceptance-to-Test Matrix

| Acceptance criterion | Evidence |
| --- | --- |
| AC1 parser/security | `TaskDeepLinkTests`: 10/10 |
| AC2 cold queue после готовности repository | startup queue в `App`, build + ViewModel navigation test; ручная installed-HKCU проверка не выполнялась |
| AC3 warm activation ровно один раз | `TaskDeepLinkActivationBrokerTests`: 1/1; Headless navigation: 1/1 |
| AC4 missing task сохраняет выбор и локализуется | `MainWindowViewModelTests`: 1/1; EN/RU resource build |
| AC5 install/update/uninstall contract | registry tests 2/2; packaging contract 1/1; реальный installer smoke не выполнялся |
| AC6 внутренняя ссылка не регрессирует | существующий Headless task-reference сценарий: 1/1 |

## Expected User Review Objections

- «Ссылка потеряется, если первый процесс не ответит» — fallback запускает новый экземпляр и передаёт ему исходный URI через startup queue.
- «Обновление сломает путь в registry» — команда указывает на launcher в `RootAppDir`, а install/update hooks перерегистрируют схему.
- «Uninstall удалит чужую схему» — удаление выполняется только при совпадении owner marker с ожидаемым launcher.
- «Откроется одноимённая задача из другого пространства» — межпространственный поиск намеренно запрещён; показывается ошибка текущего пространства.

## Quality gate и review

- Post-SPEC: **PASS** — решение переиспользует существующую навигацию Feed→Task, не меняет Markdown и task storage, а OS-specific мутация изолирована в Windows desktop layer.
- Post-EXEC: **PASS с явно ограниченным installed-smoke**.
  - Scope/Evidence: проверены утверждённая SPEC, relevant diff, `git status --short`, `git diff --check`, parser/IPC/registry/packaging/ViewModel/UI tests и сборки Desktop/Test/Headless.
  - Contract: cold/warm routing, active-space resolution, локализованная ошибка, stable launcher и ownership-safe uninstall соответствуют scenarios и AC matrix; реальная HKCU в тестах не изменялась.
  - Adversarial risk: исправлены потеря URI при IPC fallback, thread-affinity именованного mutex, ошибки инициализации ownership, dispose/pipe race и nullable registry key. Неканонические URI отклоняются.
  - Role-based review:
    - UX: открывается существующая карточка без новой поверхности; отсутствие задачи объясняется на текущем языке.
    - Tester: целевые проверки зелёные; полный параллельный suite дал 1766/1774 и 8 UI/storage isolation failures, все 8 затем проверены изолированно: hydration 1/1, dialogs 5/5, stale settings test исправлен и класс 4/4.
    - Developer/architect: platform-specific registry изолирован в Desktop, общий parser не зависит от Windows, IPC принимает только валидированный task URI.
    - Delivery/security: install/update/uninstall hooks и owner guard проверены без записи в реальную HKCU; installer/Obsidian smoke остаётся release-stage проверкой.
  - Fix and re-review: после lifecycle hardening повторены Desktop/Test/Headless builds, 15 deep-link unit/contract tests и два Headless navigation tests — green.
  - Independent reviewer: не запускался, поскольку текущий orchestration mode запрещает subagent без явного запроса пользователя; выполнен отдельный adversarial self-review.
  - Unrelated changes: сохранены ранее существовавшие изменения порядка разбора; они не откатывались и не приписываются этой SPEC.
  - Stop decision: **PASS** для локальной реализации. До release требуется installed-package smoke с временной тестовой установкой: registry command, cold click из Obsidian, warm activation и uninstall cleanup.

## Approval

Пользователь подтвердил SPEC точной фразой «Спеку подтверждаю». Разрешены реализация и локальные проверки; commit/push/PR, публикация installer и изменение реальной пользовательской HKCU-регистрации во время тестов не разрешены.

## Журнал действий агента

| Фаза / событие | Решение | Evidence / остаток | Следующий шаг | Фактическое решение человека, если требовалось |
| --- | --- | --- | --- | --- |
| SPEC, 2026-09-22 | Выбран Windows-first внешний deep-link pipeline с cold/warm activation и переиспользованием карточки задачи | Внутренние task URI уже работают; `Program.Main` не маршрутизирует URI; protocol/single-instance отсутствуют; Windows package — Velopack | Получить exact approval, затем TDD → core parser/coordinator → desktop IPC/registry → UI/package tests → post-EXEC review | «Спеку подтверждаю» |
| EXEC, 2026-09-22 | Реализованы строгий parser, cold/warm activation, Windows protocol registrar, навигация в карточку задачи и локализованная ошибка | Parser 10/10, registry 2/2, IPC 1/1, packaging 1/1, ViewModel 1/1, новый Headless UI 1/1 и существующая внутренняя task-link навигация 1/1; Desktop/Test/Headless builds green | Выполнить post-EXEC review и отделить installed smoke от локальных тестов | Подтверждение реализации получено через approval SPEC; commit/push не запрашивались |
| Post-EXEC, 2026-09-22 | PASS для локальной реализации после adversarial review и fix/re-review | Полный parallel suite: 1766/1774; все 8 падений изолированы и перепроверены green, stale `SettingsTabItem` test обновлён под `GlobalSettingsButton`; финальные целевые проверки green | Перед release выполнить installed-package/Obsidian smoke; не коммитить без отдельной просьбы | Дополнительное решение пользователя не требовалось |
