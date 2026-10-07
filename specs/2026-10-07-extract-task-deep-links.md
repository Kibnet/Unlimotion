# Изолированный перенос ссылок на задачи из daily-feed

## 0. Метаданные

- Фаза: EXEC, подтверждена пользователем 2026-10-07.
- Форма: expanded; medium — публичный URI, OS-регистрация и межпроцессная активация.
- Профили: dotnet-desktop-client, ui-automation-testing; context testing-dotnet.
- Stack: central AGENTS/routing, creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration/testing-baseline, quest-governance/mode, spec-linter/rubric/review-loops, локальный AGENTS.override.
- Canonical template: `C:/Users/Kibnet/.codex/agents/templates/specs/_template.md`.
- Поверхность: Codex Desktop; effective model/effort/version не проверены, baseline не является заявлением о runtime. Model eval не применим: меняется приложение, не модель.
- Ветка: `feat/task-deep-links`.
- Worktree: `C:/Users/Kibnet/.codex/worktrees/task-deep-links/Unlimotion`.
- База: актуальный после fetch `origin/main`, `6cf58f89479a8d45348f5c0d1c96af22ed81a579`.
- Источник: `feat/daily-feed`, коммиты `1a4d9845`, `507747aa`; поздние изменения источника используются только при необходимости для корректности ссылок.
- Разрешено: создать отдельный worktree и подготовить перенос. Commit/push/PR/merge/release/установка не запрошены.

## 1. Overview / Цель

Исходное поручение: начать с чистого worktree актуального main и перенести функцию 8 — ссылки на задачи.
Пользователь нажимает в Obsidian `unlimotion://task/feed-12422d3acca249db950bccce95f0d723` и видит соответствующую обычную карточку задачи в Unlimotion.
Success means: независимая от ленты реализация, работающая при холодном запуске и в уже работающем приложении, с корректной ошибкой отсутствующей задачи.
Артефакт EXEC: локальный проверенный change set только этой функции, новые тесты и evidence. Остановка: после обязательных проверок либо при конкретном blocker; выпуск и слияние не выполняются.

## 2. Текущее состояние (AS-IS)

Чистый worktree создан от main, затем создана отдельная ветка. До SPEC git status был пуст.
В main нет TaskDeepLink activation wiring. В `MainWindowViewModel` уже есть `FindTaskById`, `CurrentTaskItem`, `DetailsAreOpen`, `SelectCurrentTask` и существующая карточка.
В source есть parser, Windows registrar, desktop broker, Avalonia activation adapter и платформенные manifests. Первый source-коммит уже зависит от `SelectedWorkspaceMode`, которого не следует переносить. Поздний source App вызывает новую workspace-навигацию.
Нельзя копировать целиком App/MainWindowViewModel/Program или cherry-pick с принятием связанных feed/workspace изменений.
Общий брокер в source имеет глобальное имя канала: тесты кандидата не должны отправлять команды пользовательскому экземпляру.

## 3. Проблема

Полезная автономная функция находится внутри непригодной к выпуску большой ветки и связана с её новой навигацией.

## 4. Цели дизайна

Сохранить URI-контракт; переиспользовать main-карточку; отделить платформенный вход от UI; не менять модели задач, сервер и данные. Иметющиеся изменения main сохраняются.

## 5. Non-Goals

Лента, Markdown, заметки, области, IsGoal, Eremex, новые панели/вкладки, история workspace, копирование ссылок новым UI, смена пространства по URI, поиск задачи по всем базам, новые пакеты, обновление AppAutomation, публикация/установка.
Рабочая база пользователя и регистрация его установленного приложения не изменяются при проверках.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `TaskDeepLink`: разбор одного task ID и валидация URI.
- `TaskDeepLinkActivationBroker`: локальная доставка desktop cold/warm request, pending queue и освобождение ресурсов.
- `WindowsTaskProtocolRegistrar`: регистрация установленного приложения через lifecycle Velopack, безопасное удаление только собственного handler.
- `AvaloniaTaskDeepLinkActivationSource`: platform activation и pending events для macOS/Android.
- `App`: очередь до готовности VM, доставка на UI dispatcher, сообщение ошибки, восстановление/активация окна.
- `MainWindowViewModel`: открытие существующей задачи через main-навигацию; без WorkspaceLocation/Feed.
- `.desktop`, Info.plist, Android Activity: декларация scheme и передача URI.

### 6.2 Детальный дизайн

Извлечь автономные новые классы из исходных двух коммитов, интеграционные фрагменты адаптировать вручную к main.
URI содержит ID, не имя файла и не команду. Parser сохраняет ограничение 160 символов и допустимые ASCII letters/digits, `-`, `_`; query, fragment, userinfo и дополнительные сегменты отвергаются.
Искать только в активном пространстве. Не загружать по ID произвольный путь и не создавать отсутствующую задачу. Фильтры не должны мешать показу карточки; их настройки не сбрасываются ради перехода.
До `IsInitialized` событие сохраняется и обрабатывается после готовности. Наличие startup URI в args и platform activation не должно давать двойной обработки одного события. Ошибки async routing обрабатываются, а не уходят в unhandled async-void.
При неудаче найти задачу — локализованное уведомление RU/EN, прежняя карточка остаётся. Фокус окна — best effort по правилам ОС; это не гарантия принудительного foreground.
UI не блокируется ожиданием IPC; timeout передачи ограничен. Dispose снимает подписки и освобождает ownership.
Visual planning artifact: отдельный макет не применим — компоновка main не меняется. Текстовый storyboard: «Obsidian → URI → существующее окно/запуск → обычная карточка с нужными ID/названием»; ошибка: «та же карточка + локализованное уведомление».
Видео: before не применимо — обработчика в main нет; after записать автоматизированным UI runner при поддержке. Если технически невозможно, указать точную причину, команду, trace/log и просмотренный Headless PNG как fallback. Бинарные artifacts не коммитить.

### 6.3 User-Observable Scenarios

| Сценарий | Действие | Видимый результат | Evidence | AC |
|---|---|---|---|---|
| Холодный запуск | Нажать валидную ссылку | После загрузки открыта задача | Startup integration + UI | 1,2 |
| Тёплый запуск | Нажать ссылку при открытой другой задаче | Нужная карточка в существующем приложении | Broker + UI | 2,3 |
| Свёрнутое окно | Повторить переход | Окно восстановлено, нужная задача | Windows native smoke | 3 |
| Не найдено | Ссылка на неизвестный ID | Ошибка, без создания и смены базы | UI и read-back fixtures | 4 |
| Невалидный URI | Query/path traversal/другой host | Без открытия/побочных действий | Parser tests | 5 |
| Платформы | URI через OS handler | Передача task ID в App | Packaging contracts, native gates | 6 |

### 6.4 State / Interaction Matrix

| Состояние | Trigger | Результат | Ошибка/конкуренция |
|---|---|---|---|
| VM ещё загружается | URI | Очередь до готовности | Failed initialization не вызывает поиск в пустой базе |
| VM готова | URI | Обычная main-карточка | NotFound оставляет прежний объект |
| Desktop уже работает | Второй процесс с URI | Доставка первому, выход второго при успешной передаче | Timeout не теряет URI молча |
| Свёрнутое окно | Доставленный URI | Normal/show/activate | Ограничение foreground ОС сообщается отдельно |
| Dispose | Позднее событие | Нет маршрутизации в disposed VM | Проверка подписок/очередей |

### 6.5 Decision Ledger

| Решение | Владелец | Выбрано | Confidence | Риск | Нужно решение до EXEC |
|---|---|---|---:|---|---|
| Чистая база | user | Актуальный main | 1.0 | Нет переноса unrelated кода | Нет |
| Платформенный scope функции 8 | agent | Windows/Linux/macOS/Android из inventory | 0.95 | Runtime на чужих ОС не проверяется с Windows | Нет |
| Поиск задачи | agent | Только активное пространство | 0.95 | Чужая база выдаёт NotFound | Нет |
| UI | agent | Обычная карточка main | 1.0 | Связь с source workspace исключена | Нет |
| Live handler при тестах | agent | Только изолированные fixtures; установку не делать | 1.0 | Не доказана установленная интеграция | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Контракт | Source of truth | Изменение | Совместимость | Проверка |
|---|---|---|---|---|
| URI | TaskDeepLink | task ID parser | Source URI сохранён | Unit invalid/valid |
| Task data | main storage | Нет | Старые JSON без миграции | Fixture read-back |
| Windows handler | HKCU lifecycle | Register/unregister installed launcher | Не удалять чужого owner | Fake registry, quoted paths |
| Linux/macOS/Android | packaging manifests | URI scheme metadata | Прочие параметры main сохранены | Contract/generated manifest |
| IPC | Broker | Ownership/pending/forward | Тестовый канал уникален | Two brokers integration |

## 7. Бизнес-правила / Алгоритмы

Validate → queue until ready → find in active task space → open main card or notify → restore/activate window. Повторные пользовательские нажатия допустимы, задача не создаётся. Exactly once относится к одному доставленному событию, не к вечной дедупликации одинакового URI.

## 8. Точки интеграции и триггеры

Program args и Velopack hooks; OnFrameworkInitializationCompleted; переход VM в initialized; platform Activated; выход/dispose. Main `SelectCurrentTask` вызывается после выбора найденной задачи, без переноса mode-switch source.

## 9. Изменения модели данных / состояния

Только runtime pending queue и activation subscriptions. TaskItem, DTO, task status, источники задач и persisted settings не меняются. Windows registry меняется только установленным lifecycle, не unit/headless тестом.

## 10. Миграция / Rollout / Rollback

Нет миграции базы. Windows lifecycle использует стабильный launcher вне version directory; uninstall удаляет только зарегистрированный собственный owner. Для packaging сохранить main flags.
Локальный откат — отмена отдельного change set. Installed rollback требует соответствующей lifecycle очистки, не выполняется этой задачей. Автоматическая перерегистрация portable/debug приложения исключена.

## 11. Тестирование и критерии приёмки

AC1: cold request не теряется до готовности. AC2: видимая карточка имеет правильные ID/title, включая отфильтрованную задачу. AC3: warm delivery не запускает второй рабочий экземпляр; окно восстанавливается. AC4: missing ID даёт локализованную ошибку без изменений task data/space. AC5: invalid input не маршрутизируется. AC6: manifests/registrar соответствуют URI контракту. AC7: diff не содержит Notes/Feed/Workspace/IsGoal изменений; обычные сборки и обязательные suites зелёные.

### Acceptance-to-Test Matrix

| AC | Automated test/check | Manual / evidence | Непроверенная граница |
|---|---|---|---|
| 1 | Startup pending + activation before subscriber integration | Trace в fixture | Installed cold click отдельно |
| 2 | MainWindowViewModel tests + AppAutomation main card flow | Открыть новый rendered PNG нужной карточки | Assertion VM недостаточен |
| 3 | Two-broker forwarding tests + window state integration | Windows native isolated smoke/video при доступном desktop | Нельзя отправлять production broker |
| 4 | Missing ID / RU-EN UI assertions + fixture unchanged | Уведомление и прежняя карточка | Не меняем user dataset |
| 5 | TaskDeepLinkTests valid/invalid cases | Parser diff review | Нет shell execution |
| 6 | Windows registration/activation/packaging tests | Generated manifests и builds | Native Linux/macOS/Android smoke перед release обязателен, здесь отдельно ограничен |
| 7 | Full core + Headless suites, git diff audit | Обычный desktop Debug/Release | Native/CI не подменять локальными результатами |

Команды из root после toolchain preflight: `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug`, аналогично Release; Debian/macOS project builds; Android build при доступном workload (иначе явно blocked). TUnit: `dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj --treenode-filter "/*/*/TaskDeepLinkTests/*" --maximum-parallel-tests 1`; аналогично activation/registration/packaging classes. UI: соответствующий метод в `tests/Unlimotion.UiTests.Headless` с `--treenode-filter` и serial execution.
Обязательны полный `src/Unlimotion.Test` и Headless suite: область App startup/security/OS config. Сборки общих проектов выполнять последовательно; независимые процессы тестов с `--no-build` допустимы одновременно. Лог/TRX в ignored `artifacts/deep-links`; targeted green не равен full green. При hang изучить progress/process/log, не повторять неизменённую команду и не ослаблять assertions ради PASS.

## 12. Риски и edge cases

IPC isolation от пользовательского экземпляра; pending при неуспешном connect; task filters hiding target; lifetime dispose; shell quoting; конкурирующие activations; отсутствие native окружения целевых ОС.

### Expected User Review Objections

| Замечание | Почему вероятно | Решение | Статус |
|---|---|---|---|
| Снова затащили ленту | Source интеграция связана с Feed | Whitelist перенос, no new Notes/Eremex/Workspace refs | mitigated |
| Тест открыл мою задачу в боевой программе | Broker name общий | Уникальные тестовые каналы и изолированные настройки | mitigated |
| На всех ОС только декларации | Windows host | Native gates отдельно от contracts/builds | accepted-risk: не claiming native readiness |
| Отфильтрованная задача не открывается | Main selection связан со списками | UI assertion правильного ID без сброса фильтров | mitigated |

Rework checklist: сценарий и видимые состояния заданы; evidence для каждого AC есть; решения и исключения названы; роли проверены; native smoke не заменён статикой.

## 13. План выполнения

После exact approval: автономные source классы → main wiring и manifests → новые/адаптированные tests → targeted + rendered evidence → standard builds → full suites → post-EXEC audit. Исходный eff3 worktree и его dirty tests не трогать.

## 14. Открытые вопросы

Нет блокирующих продуктовых решений. Exact approval получен, выполняется EXEC. Недоступность platform smoke фиксируется как граница проверки, а не выдуманный успех.

## 15. Соответствие профилю

dotnet-desktop-client: async platform boundary, main navigation, build/test. ui-automation-testing/AppAutomation skill: реальный UI flow, stable selectors, inspected PNG и video/обоснованный fallback. Work-alone сохраняется; независимый reviewer не запускается из-за запрета делегирования пользователя, отдельный adversarial self-review не называется независимым.

## 16. Таблица изменений файлов

| Файлы | Изменение | Причина |
|---|---|---|
| Unlimotion/TaskDeepLink.cs и AvaloniaTaskDeepLinkActivationSource.cs | Новые автономные классы | URI/OS вход |
| Desktop/Services/TaskDeepLinkActivationBroker.cs, WindowsTaskProtocolRegistrar.cs | Новые классы | Desktop IPC/registration |
| Desktop/Program.cs; Unlimotion/App.axaml.cs | Точечное wiring | Startup/warm/lifecycle |
| ViewModel/MainWindowViewModel.cs; Resources/Strings*.resx | Main open method/error | Существующая карточка |
| Desktop/ci/deb/unlimotion.desktop, ci/osx/Info.plist; Android/MainActivity.cs | Metadata | Scheme integration |
| src/Unlimotion.Test/*DeepLink*Tests.cs, *Protocol*Tests.cs и VM tests | Contracts | Positive/negative cases |
| tests/Unlimotion.UiTests.Authoring/Headless/FlaUI соответствующие сценарии | Main UI coverage | Не переносить feed сценарии |
| README.md/README.RU.md при необходимости | Краткое описание URI/platform границ | Не переносить feed documentation |

## 17. Таблица соответствий (было → стало)

| Область | Main сейчас | После переноса |
|---|---|---|
| URI | Нет handler | Вход к main-карточке |
| Навигация | Обычная main | Та же, без workspace redesign |
| Заметки/области | Не относятся к change set | Без изменений |

## 18. Альтернативы и компромиссы

Cherry-pick двух полных commits быстрее, но включает source интеграцию/спеки с зависимостями; выбран выборочный перенос и адаптация. Windows-only меньше, но не сохраняет функцию 8 целиком; переносим платформенные adapter/metadata без ложного заявления native smoke. Полный redesign ради открытия задачи исключён.

## 19. Результат quality gate и review

### SPEC Linter Result

A: 1 PASS outcome URI→card; 2 PASS main/source inspection; 3 PASS isolation problem; 4 PASS goals; 5 PASS Non-Goals.
B: 6 PASS responsibilities; 7 PASS startup/ready/dispose hooks; 8 PASS routing; 9 PASS missing/invalid/timeout; 10 PASS async bounded IPC.
C: 11 PASS runtime-only state; 12 PASS preserved data/URI; 13 PASS owner-aware unregister/local rollback.
D: 14 PASS AC1–7; 15 PASS matrix with negative cases; 16 PASS runner/commands/log/stop.
E: 17 PASS extraction dependencies; 18 PASS decisions recorded; 19 PASS expanded medium justification.
F: 20 PASS profiles/UI skill. Итог: готово к утверждению SPEC, не свидетельство EXEC.

### SPEC Rubric Result

| Критерий | Балл | Основание |
|---|---:|---|
| Цель/границы | 5 | Только URI→main card |
| AS-IS | 5 | Main и source wiring проверены |
| Дизайн | 5 | Platform/VM/App boundaries |
| Безопасность | 5 | Parser whitelist, isolated tests, owner-aware registration |
| Проверяемость | 5 | AC map, UI/full suites, platform gates |
| Автономность | 5 | Решения/порядок/stop записаны |

30/30 — оценка SPEC, не гарантия качества реализации.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
|---|---|---|---|---|
| Business analyst | applicable | Открывается нужная задача из Obsidian? | PASS | Active-space restriction записан |
| UX / designer | applicable | Карточка и ошибка очевидны? | PASS | Existing main UI, filters case |
| Tester | applicable | Проверены cold/warm/invalid/missing? | PASS | AC matrix, native gate |
| Developer / architect | applicable | Есть ли скрытая Feed dependency? | PASS | Main-specific routing вместо source mode |
| Delivery/security | applicable | Нет registry/IPC user side effects от тестов? | PASS | Fixtures/unique channels, no installation |

### Post-SPEC Review

- Scope reviewed: эта SPEC, central owners/profile, main VM/Program, source two-commit stats и App/VM diff, registrar/broker; git clean/base SHA/new branch; worktree attachment result.
- Scope/Evidence pass: source implementation доступна; main требует адаптации, а не whole-file copy. База после fetch совпала с HEAD worktree.
- Contract pass: URI и существующая карточка сохранены; no-feed whitelist и отсутствие task migration зафиксированы.
- Adversarial pass: контрпримеры — двойная cold delivery, URI до готовности, фильтр скрывает задачу, тестовый broker достигает production, uninstall чужого owner. Внесены explicit checks/isolation.
- Role-Based pass: таблица выше; самостоятельный review, не независимый.
- Findings: MEDIUM integration — source зависит от workspace; required action manual main adaptation; status fixed in plan. MEDIUM testing — shared broker мог затронуть production; action unique test channel; status fixed in plan.
- Fix and re-review: перепроверены source parser/registration и main selection API; план не требует feed типов, installed lifecycle не запускается тестом.
- Depth checklist: unrelated diff исключён; AC/negative evidence заданы; platform claim ограничен; no schema migration; docs только URI; manual-review challenge — warm IPC и unsaved/current-task navigation должны проверяться на main UI, не только VM.
- No-findings justification: нет оставшихся blocker SPEC; runtime native readiness и текущая безопасность IPC требуют фактической EXEC проверки и не объявляются доказанными.
- Stop decision: PASS для запроса exact approval; до него только эта SPEC изменена.

### Post-EXEC Review

Перенесены автономные URI/platform классы; навигация адаптирована к обычной main-карточке. При self-review исправлены холодный startup override и неограниченное ожидание молчащего IPC-клиента (broker RED 0/1 → GREEN 2/2). Нормализация `Uri` могла скрыть dot-segment до валидации: regression RED 0/1, исправление через OriginalString, target GREEN 33/33 Release. Последняя правка ставит launch URI перед buffered warm requests; она проверяется дополнительным contract test и повторным target.

Первичный target: 31/31, последующий broker target: 2/2. Windows native cold/warm/minimized smoke: 1/1, изолированный канал/fixture, без установки/registry writes. AppAutomation 1.9.0: полный Headless GREEN 56/56 (full-headless-final.log), включая реальное отсутствие задачи в отфильтрованном списке, открытие её карточки без сброса фильтра, RU/EN missing-task. Rendered PNG просмотрены. Эти прогоны предшествуют последней перестановке очереди startup URI.

Обычные сборки Desktop Debug/Release, Debian Release, macOS Release и Android Debug android-arm64 прошли. Последний Desktop Debug после перестановки startup queue: 0 ошибок, 3 предупреждения в неизменённых ServerStorage/TaskStorageBuilder/ConflictResolutionControl. Debian ранее: NETSDK1206; Android: 25 предупреждений (NU1608, CA1416, XA0141, XA4301), без обновлений зависимостей. Остальные platform builds предшествуют последней перестановке startup queue. Native Linux/macOS/Android и installed Windows association не проверены.

Полный core Release завершён: 1394 passed / 4 failed / 1398 total, 32m08s (full-core-final.log и TRX 2026-10-07_13_32_17). Сбои: TaskCardStatusRecovery_ErrorAndReloadRemainVisible ru/Light при 760 и 1400 (ширина ошибки 0); TaskCardLayoutScenario_ExecutesFeatureSteps (ширина карточки 0); MeasuredBenchmark_3000Tasks600ReadsVsCaptureAndOfflinePages (deadline observation 60s). Эти файлы не изменены, но baseline main не прогонялся: статус «старые сбои main» не доказан. Отдельный повтор status-layout matrix: 8/8, что не отменяет общий RED. Во время конца полного прогона ошибочно запущена Release пересборка target: file locks/MSB3021, лог сохранён, результат не засчитан; target повторяется отдельно. Benchmark проходил при нагрузке сборки, влияние нагрузки не исключено. Full core предшествует последней перестановке startup queue. Общий acceptance gate остаётся RED, unrelated тесты не переписаны и не отключены.

Повторный target на последнем коде: GREEN 34/34 Debug (targeted-latest-debug.log и TRX 2026-10-07_13_34_02), включая startup-order contract, parser, activation sources, coordinator, broker и UI. Повторный Windows FlaUI на последнем коде: GREEN 1/1, 31.88s (native-latest.log и TRX 2026-10-07_13_35_16): cold launch открывает target вместо ранее выбранной задачи, warm URI передаётся владельцу без второго окна, minimized owner восстанавливается. Контракт порядка очереди проверяет wiring исходника, не заменяет native тест одновременных cold/warm запросов во время загрузки.

Ранние попытки Headless не выдаются за PASS: были собственные compile errors, отсутствие frame при PerTest-dispatch и hang при переключении сессий; сохранены logs/dumps. Финальный rendered сценарий вынесен в свежий процесс, исходные semantic/recovery hooks сохранены. Native PNG/video захват оказался некорректен из-за DPI и включал фон за окном; такие собственные артефакты удалены. Fallback: просмотренные rendered Headless PNG и native UIA assertions/TRX. Нет валидной пары native video до/после; до-функция в main отсутствовала.

Self-review: diff ограничен URI/platform/navigation/localization/tests/docs; нет переноса Feed, Areas, IsGoal removal, docking или зависимости Eremex. Формат task JSON не меняется. Ошибки отсутствующей задачи не создают данные и не переключают space. Негативные parser/timeout/late-dispose cases добавлены. git diff --check чист. Review самостоятельный, не независимый. Stop decision: scoped implementation выполнена, но публикация/полная acceptance не одобрена — общий core gate RED и установочные platform flows не проверены; отдельное исправление несвязанных full-suite сбоев требует решения о расширении scope.

## Approval

Получена фраза: «Спеку подтверждаю» (2026-10-07). Это разрешение на EXEC, не на commit/push/PR/merge/release/установку.

Отдельное поручение 2026-10-07: «Оформи pr и влей в мастер» разрешает commit/push/PR и слияние в default branch `main`. Установка и выпуск не запрошены. Перед слиянием требуется проверить exact-head CI; локальные 4 full-suite сбоя явно раскрываются в PR, обязательные проверки не обходятся.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
|---|---|---|---|---|---|
| SPEC 2026-10-07 | Создан clean managed worktree и feat/task-deep-links от fetched main | HEAD 6cf58f89, до SPEC status пуст; source dependencies изучены | Exact approval, затем изолированный перенос | User попросил отдельный worktree и функцию 8 | Эта SPEC; исходный eff3 сохранён |
| EXEC 2026-10-07 | Перенос только URI/platform activation в main UI | Exact approval получен; код и проверки в работе | Перенос и staged validation | «Спеку подтверждаю» | Код ссылок, tests, эта SPEC |
