# Unlimotion: переход на опубликованный AppAutomation 1.9.0

## 0. Метаданные и разрешения

- Фаза EXEC, ограниченное продолжение [согласованной pointer SPEC](https://github.com/Kibnet/AppAutomation/blob/v1.9.0/specs/2026-10-02-desktop-pointer-ownership.md). Пользователь ранее сказал «Спеку подтверждаю», затем «Оформи PRы», теперь — «Теперь в Unlimotion обнови пакет».
- Владелец: Павел. Ветка `fix/ui-pointer-ownership`, PR #313, исходный commit `3a885b61d0a7f99f2a1c45427643623df1bf7289`; рабочее дерево чистое. Основной пользовательский checkout не меняется.
- Форма expanded по central `templates/specs/_template.md`: меняется доставка зависимости через NuGet и существующий PR. Само уточнение не меняет утверждённый API или риск управления указателем и не сбрасывает EXEC.
- Stack: central AGENTS/routing, model-behavior/tool-execution/collaboration/testing baselines, QUEST/review-loops, testing-dotnet, dotnet-desktop-client, ui-automation-testing, GitHub delivery/versioning; локальный AGENTS.override. Skills: appautomation, run-tunit-tests.
- Runtime: Codex desktop, Windows/PowerShell, SDK 10.0.401 (pin 10.0.400/latestPatch), TUnit 1.44.0, Avalonia 12.0.4. Model/prompt evaluation не применим.

## 1–3. Цель, AS-IS и проблема

Исходная задача — исключить ненужную парковку мыши в углу и возвращать её после click/hover/drag. Миграция UI-тестов уже находится в PR #313, но 11 references в пяти проектах используют локальный `1.8.1-pointer.20261002.2`. Обычный hosted restore не получает эту версию. AppAutomation #39 влит, стабильный 1.9.0 опубликован и проверен публичной установкой.

Текущий outcome: согласовать все ссылки с опубликованным 1.9.0, проверить публичный restore и сборку затронутых проектов, повторить Headless suite и обновить evidence/PR. Полную готовность исходной UI-миграции нельзя объявлять при остающихся ошибках suite.

## 4–6. Решение и границы

- Заменить только версии AppAutomation в TestHost, Authoring, Headless, FlaUI и ReadmeMedia. Не менять SDK, TUnit, Avalonia и продуктовый код.
- Сохранить существующие UI assertions и pointer helpers. Не исправлять HTTP→SSH, отсутствующий Repeater или raw Mouse flows генератора ReadmeMedia в рамках обновления зависимости.
- Проверять restore с явным `https://api.nuget.org/v3/index.json`; проверить resolved assets и происхождение AppAutomation packages.
- Сборки выполнять последовательно: общие ProjectReference используют одни output directories.
- Visual planning: не применимо к замене версии — layout и сценарии не меняются. Предыдущие before/after videos и trace связаны в отчёте; новый физический прогон возможен только при доступном интерактивном desktop. Последняя проверка desktop завершалась потерей foreground/input ownership, поэтому fallback этого шага — build FlaUI, Headless suite и опубликованные framework CI/evidence, без нового утверждения о native PASS.

### User-Observable Scenarios

| Сценарий | Ожидаемый результат | Evidence / AC |
| --- | --- | --- |
| Разработчик восстанавливает и собирает UI-тесты из PR | Достаточно публичного NuGet, локальная pointer prerelease не требуется | Restore, assets, Release builds; AC1–2 |
| Запуск существующих Headless сценариев | Реальные результаты записаны, известные ошибки не скрыты | Full Headless log/TRX; AC3 |
| Просмотр PR | Опубликованная зависимость и текущие ограничения описаны точно | PR readback; AC4 |

### Decision Ledger / Runtime Contract

| Решение | Основание и риск | Проверка |
| --- | --- | --- |
| Единая версия 1.9.0 для всех 11 references | Опубликованная версия согласованного framework; смешанные версии недопустимы | XML inventory и assets |
| Существующий draft PR #313 | Ранее разрешены PRы; новые продуктовые исправления и merge не входят в текущий запрос | Branch/upstream, PR readback |
| Full Headless, builds всех пяти проектов через три верхних проекта | Область изменения — библиотека UI-тестов; unit/product source не меняется. CI отдельно запускает main и Headless suites | Logs и CI metadata |

State/interaction matrix, бизнес-алгоритмы, storage/data migration: не применимо, этот шаг меняет package resolution существующих сценариев.

## 10–12. Проверки, риски и rollback

| AC | Обязательная проверка | Evidence |
| --- | --- | --- |
| AC1: все 11 references и resolved AppAutomation libraries используют 1.9.0 | XML/JSON audit, restore из nuget.org | `artifacts/appautomation-1.9.0/` |
| AC2: изменённые проекты компилируются | Release builds Headless, FlaUI, ReadmeMedia (включая TestHost/Authoring) | Build logs |
| AC3: полный Headless повторён; ошибки отличены от restore/build | TUnit/MTP full suite, без исключения красных тестов | Headless log/TRX; не-PASS сохраняет draft gate |
| AC4: consumer PR содержит новый commit и правдивый отчёт | diff review, push и readback | PR #313 |

Основной риск — ошибочно представить устранение неопубликованной зависимости как устранение всех UI failures. Rollback: revert только этого commit; это вернёт локальную prerelease и её прежнее ограничение доставки. Данные пользователя не мигрируются.

| Возможное замечание пользователя | Ответ / ограничение |
| --- | --- |
| «Пакет опять только локальный?» | Проверяются публичный источник и resolved versions |
| «Теперь все UI-тесты зелёные?» | Сборка и результаты suite показаны отдельно; draft сохраняется при blocker |
| «Мышь снова захватят при проверке?» | Headless не использует физический указатель; native запуск без доступного desktop не выполняется |

## 13–18. Исполнение и соответствие

Порядок: обновить ссылки → публичный restore/build → Headless → diff/evidence review → commit/push/PR. Новые тесты, зеркалящие замену версии, не нужны. Существующие meaningful UI-тесты сохраняются. Существенных открытых решений нет. Исторические версии в старом evidence не переписываются как будто прошлые прогоны использовали 1.9.0.

Изменяемые файлы: пять `tests/*/*.csproj`, этот consumer SPEC и `docs/validation/2026-10-02-pointer.md`. Обновление описания PR отражает итоговый результат. Альтернатива оставить prerelease отклонена: публичный restore останется сломан.

## 19. Review

Post-SPEC continuation review: PASS для ограниченного перехода на опубликованную версию. Scope/evidence: проверены PR/HEAD, 11 references, SDK и CI workflow. Contract: этот шаг реализует прежнюю dependency gate и прямое поручение пользователя. Adversarial: локальный package cache не должен подменить nuget.org; сборка не доказывает native UI. Roles: developer — единые зависимости; tester — full Headless без скрытых skips; delivery — существующая ветка/draft; UX — исходное pointer evidence не заменяется сборкой. Нет находок, требующих изменения согласованного решения. Независимый reviewer для этого ограниченного metadata-only продолжения не требуется; исходный large pointer контракт ранее прошёл отдельный review.

Post-EXEC: **NEEDS-FIX для общей готовности UI-миграции**; замена зависимости и её доставка в draft PR проверены локально. Scope/evidence pass: diff содержит 11 замен версии в пяти csproj, этот SPEC и validation report; три публичных restore, три Release builds, аудит 22 entries в пяти assets files и полный Headless TRX прочитаны. Contract pass: AC1–2 выполнены; AC3 исполнен без исключения тестов, но green не получен; AC4 проверяется push/readback. Adversarial pass: источники всех шести packages — nuget.org, mixed versions нет; native video на prerelease не выдаётся за новую проверку; updater не трогает пользовательское хранилище. Role-based pass: developer — API/source не изменён; tester — сохранён failure и native gap; delivery — только текущая ветка, PR draft, без merge; UX — исходная цель возврата мыши остаётся предметом предыдущего evidence. Fix/re-review: из отчёта убрана устаревшая dependency gate, исторические прогоны помечены явно, diff check чистый. Stop: завершить доставку package bump, сохранить общий UI blocker без неподтверждённого PASS.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| BLOCKER | UI readiness | Headless 50 PASS / 1 FAIL / 0 skips, 2m28s; HTTP→SSH state timeout воспроизведён на 1.9.0 | Отдельно устранить продуктовую причину и повторить suite до merge | open, вне package bump |
| MEDIUM | Native evidence | Новый FlaUI запуск не выполнен из-за последнего отказа владения desktop; старая Repeater ошибка остаётся незакрытой | Повторить native validation при доступном desktop после разрешения исходных blockers | open, draft gate |

Depth checklist: unrelated изменений нет; references/assets/source проверены; AC/evidence сопоставлены; различие build/native test и исторических/current результатов сохранено; rollback и known failures описаны; скрытых изменений UI/API/config поведения в diff нет. Полный unit suite и hosted CI green не заявляются. No-findings justification не применяется: findings выше сохраняются.

## 20. Исправление HTTP→SSH после разрешения пользователя

2026-10-04 пользователь спросил «Как можем исправить?», получил конкретное предложение переставить `ReloadGitMetadata()` перед присваиванием результата и ответил «Пробуй». Это прямое разрешение выполнить предложенную правку, проверки и продолжить ранее разрешённое обновление PR. Ограничение предыдущего package-only этапа на HTTP→SSH снимается только для этой ошибки; остальные non-goals сохраняются. Фаза EXEC.

AS-IS: `ApplyRemoteConnectionTypeSwitch` публикует новое `GitRemoteName` раньше `RemotesWithAuthType`. Открытый ComboBox может сбросить ещё отсутствующий элемент обратной привязкой. Уже есть RED на том же HEAD `a16de5df`: локальный Headless 50/51 и CI 37148466213, main 1147/1147. Проверяется причина, а не увеличение timeout.

Решение: сначала обновить metadata/list, затем присвоить возвращённые сервисом имя и URL. Если существующий regression не проходит, изучить фактическую обратную запись и рассмотреть ограниченную защиту только на время перестроения списка. Общий запрет пользовательского сброса выбора недопустим. Схема UI-состояний (visual planning fallback, layout неизменен): `origin / HTTP / TokenAuthSection` → `origin + origin-ssh / origin-ssh / SSH / SshKeysSection` → refresh сохраняет выбор → HTTP выбирает существующий `origin` без дублей.

| AC / сценарий | Evidence |
| --- | --- |
| Исходный HTTP→SSH с показанным окном работает | Прежний Headless-тест без ослабления assertions/timeout |
| При двух remotes обратное переключение и refresh сохраняют имя/URL и соответствующий режим | Дополнение UI regression; выбранный элемент самого ComboBox, отсутствие дублей |
| После применения выбранные настройки сохраняются корректно | Конфигурационный assertion в SettingsViewModelTests, без пользовательских данных |
| Не изменены остальные настройки/Git алгоритмы | Targeted SettingsViewModelTests, full main и Headless, сборка FlaUI; итоговый CI |

Производственный diff ограничен `SettingsViewModel.ApplyRemoteConnectionTypeSwitch`. Тесты в `SettingsRemoteTypeHeadlessTests` и при необходимости `SettingsViewModelTests`; evidence в текущем validation report. Rollback — revert этого отдельного fix commit. Ни сетевых push/pull Git, ни реального SSH, ни изменений пользовательского task space в тестах нет.

Video fallback: текущий Headless host использует прежний semantic renderer, а последняя native desktop проверка потеряла input ownership. Для этой правки достаточно реального control tree и выбранного ComboBox в автоматизированном Headless regression; новые pixels/video не заявляются. Исходное pointer video остаётся историческим. Полная native pointer готовность рассматривается отдельно от исправления этого CI blocker.

Post-SPEC continuation review: Scope/Evidence — метод, binding ComboBox, сервисный return contract, текущий CI и существующие tests прочитаны. Contract — имя/URL применяются после актуализации списка; локальное изменение не меняет выбор/создание remotes в Git service. Adversarial — проверить повторный refresh и обратное переключение, сохранить допустимость null при пользовательском выборе. Roles: developer — минимальный порядок событий; tester — контроль реального ComboBox и persisted settings; delivery — отдельный commit в PR #313, без merge; UX — форма и подписи неизменны. Нет неоднозначного user-owned решения. Отдельный reviewer запрашивается для config/UI границы; его статус и результаты фиксируются после получения. Post-EXEC ожидается.

Уточнение тестового окружения: один reorder не прошёл первоначальный harness. Диагностика показала выполнение continuation `ApplyRemoteConnectionTypeSwitch` через `Dispatcher.ResetForUnitTests` при завершении отдельного synchronous dispatch. В scoped regression настоящий `Button.OnClick` вызывается внутри `HeadlessRuntime.Session.Dispatch<bool>(async ...)`, который сохраняется до `IsExecuting true→false`/исключения с прежним пределом 10 секунд. Нельзя вкладывать Page/HeadlessRuntime synchronous Dispatch: он ожидает новую задачу в той же очереди; нельзя использовать нетипизированный async lambda, так как у Avalonia отсутствует overload `Func<Task>`. Reflection OnClick соответствует существующему тестовому pattern репозитория и сохраняет настоящий button command binding. Временные trace writes удалены. Negative control должен повторить исходный failure со старым product порядком при том же scoped harness.

Отдельный reviewer `review_pointer_exec`: pre-implementation PASS, технический sandbox `danger-full-access` (read-only поведение, не изоляция). Обнаруженный HIGH в первом scoped helper — неверный Dispatch overload — устранён явным `Dispatch<bool>`/`return true` до runtime проверки. `IClickableControl` оказался internal по compile evidence; используется существующий reflection pattern Button.OnClick. Повторное чтение helper не выявило blocking code findings; runtime и post-EXEC review ожидаются.

Runtime-уточнение: scoped dispatcher завершает свой Task inline на собственном queue worker. Продолжение теста затем вызывает Page helper с синхронным Dispatch и блокирует ту же очередь; это подтверждено `artifacts/remote-selection-fix/scoped-stacks.txt`. Поэтому внешний helper возвращает отдельный `TaskCompletionSource` с `RunContinuationsAsynchronously`, перенося в него success/cancellation/error. `ConfigureAwait(false)`/`ForceYielding` не используются как гарантия запрета inline continuation. Диагностические dump/trace остаются local-only.

Проверка причинности: исправленный scoped harness и product reorder дали 1 PASS (`continuation.log`); тот же harness со старым product порядком дал исходный `Selected Git remote state was not updated` / 1 FAIL (`negative-control.log`). Новый product порядок восстановлен. Assertions и исходные таймауты не ослаблены. Расширенный regression дополнительно проверяет реальный `ComboBox.SelectedItem`, refresh, SSH→HTTP с двумя remotes без дублей; отдельный unit test перечитывает имя/URL из временного config-файла после обоих переходов.

Post-EXEC code review отдельного `review_pointer_exec`: открытых code findings нет; подтверждены настоящий OnClick, async scope/continuation bridge, actual ComboBox assertions и JSON read-back. Sandbox `danger-full-access`, read-only соблюдал поведением. Targeted Headless 2/2 и SettingsViewModel 74/74 PASS, Release main/Headless/FlaUI build 0 errors. Полная локальная валидация не-PASS: Headless 19 PASS, затем suite timeout 10m/exit 3; main остановлен после >7m с тремя failures в watcher/server/directory-lock сценариях. Stack полного Headless показывает file I/O при создании MainWindowViewModel, не подтверждённый dispatcher deadlock; никакого расширения fix на lifecycle не сделано. Details/raw evidence — validation report и `artifacts/remote-selection-fix/`. Общий post-EXEC gate ожидает полную CI-проверку; отправка уже разрешённого исправления в существующий PR запускает эту проверку, не означает merge/release или новый ready transition.

## Журнал действий агента

| Фаза / событие | Решение | Evidence / остаток | Следующий шаг | Решение человека |
| --- | --- | --- | --- | --- |
| EXEC / dependency delivery | Перейти с локальной prerelease на опубликованную 1.9.0 | PR #313, AppAutomation release 1.9.0, чистый worktree | Restore/build/Headless и обновление PR | «Теперь в Unlimotion обнови пакет»; прежние SPEC/PR approvals действуют |
| EXEC / validation и review | Все 11 references обновлены; 22 resolved entries используют 1.9.0 из nuget.org; Release builds прошли | Full Headless 50/51, прежний HTTP→SSH failure; native suite не повторялся | Commit/push package bump и актуальный draft PR body; общий UI blocker сохранить | Дополнительное разрешение не требуется для согласованного package bump |
| EXEC / HTTP→SSH candidate | Проверить перестановку metadata перед выбранным remote | Существующие local/CI RED; AC и проверки в разделе 20 | Минимальная правка, regression, full validation, PR | «Пробуй» после конкретного предложения исправления |
