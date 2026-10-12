# Взять эмодзи из другой вкладки

## Контекст

- Владелец: основной агент, работа без делегирования.
- Фаза: EXEC; отдельное расширение к утверждённой [изоляции фильтров](2026-10-07-task-tab-emoji-filter-isolation.md). Её реализация находится в незакоммиченном состоянии; завершение её обязательных проверок остаётся необходимым.
- Рабочий репозиторий: `C:\Users\Kibnet\.codex\worktrees\emoji-tab-filters\Unlimotion`, ветка `feat/task-tab-emoji-filters`, inspected HEAD `e987023dc93daba6753440eec813edcbaa468da9`. Исходный большой ворктри не изменяется.
- Поручение: «придумаем как нам заполнять значения фильтров из других вкладок, чтобы это было очевидно и удобно без громоздких конструкций»; после предложения пользователь ответил «Да сделай».
- Профиль: delivery-task / .NET desktop-client / UI-automation-testing, context testing-dotnet. Central stack: routing-matrix, creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration-baseline, quest-governance/mode, testing-baseline, spec-linter/rubric, review-loops; локальный UI testing override.
- Форма: short по canonical `templates/specs/_template-small.md`. Один ограниченный обратимый UI outcome поверх существующих локальных коллекций: нет storage/config/security, публичного API, миграции или внешних действий. Общие predicates и каталог не перепроектируются. Если реализация потребует существенно нового межкомпонентного контракта, перейти к expanded до зависимых изменений.
- Runtime: Codex, Windows/PowerShell, Avalonia/TUnit/AppAutomation из репозитория. Model/prompt semantics не изменяются; фактический model ID не является критерием этой фичи, переключение не выполняется.

## Пять содержательных проверок

| Проверка | Решение / ожидаемый результат | План проверки → фактическое evidence |
| --- | --- | --- |
| 1. Результат / границы | В текущей вкладке взять включающие и исключающие эмодзи другой вкладки одним выбором, без повторного расставления галочек. AS-IS: локальные collections и фильтры уже реализованы в текущем diff; команды копирования нет. Все девять вкладок — источники и получатели, включая дорожную карту. Non-Goals: копирование других фильтров/поиска/сортировки; синхронизация вкладок; профили; сохранение настроек; редизайн компоновки; новые зависимости; Git delivery. | UI-путь ниже; результаты списка задач и галочки после применения/отмены. Inspected `TaskTabEmojiFilterState.cs`, MainWindowViewModel reset/lifecycle и bindings, MainControl/GraphControl filter panels, localization и notification wrapper. |
| 2. Решения / разрешения | Согласовано «Да сделай»: пункт «Взять эмодзи из вкладки…», два набора вместе, замена вместо объединения, snapshot вместо связи, feedback с «Отменить». Конкретизация: команда в существующей панели перед сбросом; preview каждого набора; текущая вкладка исключена; после ручного изменения получателя Undo недоступен. Существенных открытых вопросов нет. | Новое расширение требует отдельного exact approval. Ранее полученное подтверждение изоляции не разрешает копирование автоматически. Commit/push/PR/merge не поручены. |
| 3. AC→evidence | AC1–AC7 ниже; все исходные обязательные проверки из спеки изоляции сохраняются. Новые tests проверяют реальное меню и результаты, не только snapshot VM. | Новые Avalonia.Headless UI cases и AppAutomation rendered flow; existing isolation/reset/responsive regression; ordinary Desktop build и полные core/Headless suites. До EXEC это план, не PASS реализации. |
| 4. Существенный риск / rollback | Нечаянно связать mutable EmojiFilter; замаскировать источник без ограничений; перенести All так, что он затрёт частичный выбор; Undo затрёт более позднюю ручную правку или попадёт в другое пространство. Решение: наборы строковых ключей и состояние master «Все», восстановление по текущему каталогу, lifecycle invalidation. | Negative cases: источник изменён после копирования, разные частичные наборы, All в одном наборе, пустой источник, повторное копирование, ручное изменение/сброс, удаление emoji, reconnect. Откат — удалить только новый copy UI/state/tests; изоляция и данные задач остаются. |
| 5. Findings / disposition / stop | На SPEC review найдено два MEDIUM риска: существующий toast wrapper не поддерживает action; Undo после ручной правки неоднозначен. Закрыты дизайном: локальная строка feedback в открытой панели, без расширения публичного toast API; Undo инвалидируется после правки получателя. | Повторно сверены интерфейс wrapper и layout панелей: решение использует локальный UI, каталог/прогнозы не меняет. Stop SPEC: можно запросить exact approval, не заявлять фичу выполненной. |

## Пользовательский путь и визуальный план

Пример: во «Все задачи» нужно применить тот же emoji-контекст, который настроен в «Разблокированных».

1. Открыть существующую панель «Фильтры».
2. Нажать «Взять эмодзи из вкладки…».
3. Выбрать источник по названию и preview.
4. Галочки и список задач сразу изменяются; подтверждение не требуется. Source picker закрывается, основная панель остаётся открытой с feedback и Undo.

Repo-visible wireframe, не отдельный макет приложения:

```text
Фильтры текущей вкладки
┌──────────────────────────────────────────────┐
│ [существующие поля: статус, даты и т. п.]      │
│                                              │
│ Взять эмодзи из вкладки…                    ▾ │
│ ┌──────────────────────────────────────────┐ │
│ │ Разблокированные                        │ │
│ │ Показывать: 🧰 🚀 · Исключать: 💤        │ │
│ │ Выполняется                             │ │
│ │ Показывать: 🧰 · Исключать: нет          │ │
│ │ Последние созданные                     │ │
│ │ Без ограничений по эмодзи                │ │
│ └──────────────────────────────────────────┘ │
│                                              │
│ [Сбросить фильтры]                            │
└──────────────────────────────────────────────┘

После выбора (source picker уже закрыт):
│ Эмодзи взяты из «Разблокированные» [Отменить] │
```

Никаких постоянно видимых новых кнопок в toolbar. Source list имеет вертикальный скролл, ширина ограничена доступным окном; двухстрочный preview переносится и не расширяет popup. Источники в привычном порядке вкладок. Пустое включение обозначается «Все», пустое исключение — «нет»; два пустых набора — «Без ограничений по эмодзи». Длинный preview сокращается, но полный доступен в подсказке/accessible description. Названия, подсказки, feedback и automation names локализуются RU/EN. Keyboard и pointer/touch проходят стандартными controls; не зависят от right click/hover/Ctrl.

Feedback — локальная строка в панели той вкладки, в которой произошло копирование. Она видна при повторном открытии панели, пока Undo актуален; без произвольного таймера. Не используется несуществующая action-toast возможность. После Undo строка исчезает, панель остаётся открытой. Нет нового глобального стека undo.

## Контракт состояния

- Snapshot источника вычисляется **в момент выбора**, а не при создании меню. Переносятся ключи emoji и master flags обоих наборов; не Source, ссылки на EmojiFilter, hierarchy parents или состояние раскрытия.
- Новый snapshot полностью заменяет selection получателя. «Все» корректно восстанавливается без потери частичных наборов; source/получатель не разделяют mutable объекты. UI обновляется на UI-потоке, без файлового I/O и промежуточного открытия другой вкладки.
- Пустой источник снимает emoji-ограничения в получателе; preview явно предупреждает об этом. Если оба выбора уже совпадают, нет лишнего изменения/нового Undo.
- Undo хранит только состояние получателя **до последнего копирования**. Повторное копирование заменяет Undo — восстанавливается состояние непосредственно перед ним.
- Изменение источника после копирования не влияет ни на получатель, ни на его Undo. Переключение вкладки сохраняет локальный Undo; применять его можно только в своей вкладке.
- Ручное изменение emoji получателя, reset его фильтров, удаление выбранного/сохранённого для Undo emoji из каталога отменяет доступность старого Undo. Добавление emoji и переименование без изменения ключа не меняют selection и не делают Undo ошибочным; ключи которых нет в текущем каталоге не воскрешаются.
- Reconnect/disconnect/смена task space очищает все copy feedback/Undo и detached subscriptions. Переноса между пространствами нет.
- Статусы, даты, длительности, wanted-фильтры, общий поиск, сортировка, графовые режимы не копируются и не сбрасываются.

## Ответственности и план изменения файлов

- `src/Unlimotion.ViewModel/MainWindowViewModel.cs`: локальный координатор источников девяти вкладок, copy/Undo commands и lifecycle. AllTasks обслуживается через существующий owner, без переписывания canonical catalog.
- Новый локальный snapshot/helper в `src/Unlimotion.ViewModel/` либо небольшой helper рядом с `TaskTabEmojiFilterState.cs`: данные ключей и безопасное применение. Не новый публичный API/сериализованный task field.
- `src/Unlimotion/Views/MainControl.axaml`, `GraphControl.axaml` и небольшой reusable view в `Views/`: одинаковый copy picker/feedback без девяти разных реализаций. Прежние automation IDs не менять, новые copy/source/undo selectors стабильны.
- `src/Unlimotion.ViewModel/Resources/Strings.resx`, `Strings.ru.resx`: строки новой команды, preview и feedback.
- `src/Unlimotion.Test/TaskTabEmojiFilterCopyUiTests.cs` (новый): сценарии AC1–7.
- `tests/Unlimotion.UiTests.Authoring/Tests/` и `tests/Unlimotion.UiTests.Headless/Tests/`: сценарий копирования/отмены с реальным rendered capture по существующему harness.
- `src/Unlimotion.ViewModel/AssemblyInfo.cs`: friend access для Headless tests к существующему `SealPendingSaves`, чтобы изолировать синтетические данные от autosave без нового публичного API или reflection.

## AC и обязательные проверки

| AC | Проверяемый результат | Evidence |
| --- | --- | --- |
| AC1 | Команда доступна во всех 9 вкладках, показывает другие 8 вкладок и корректный preview; доступна клавиатурой и pointer | Parameterized UI cases, меню действительно открыто |
| AC2 | Выбор одним действием заменяет include/exclude, сразу меняет реальные результаты; остальные фильтры и источник неизменны | UI copy + task ID results, partial и All cases |
| AC3 | После переключения и последующей правки источника получатель остаётся независимым | Switch/modify source/return UI case |
| AC4 | Пустой источник снимает только emoji-ограничения; identical copy не создаёт ложного undo/feedback | UI empty/identical + сохранённые status/search assertions |
| AC5 | Feedback указывает источник; Undo возвращает обе исходные selections и список, только в получателе | Реальное нажатие Undo, repeated-copy и tab switching cases |
| AC6 | Ручная правка/reset/catalog removal/reconnect инвалидируют соответствующий Undo; переименование и добавление не разрушают surviving choices | UI/lifecycle regression; detached source check |
| AC7 | На 900 и 390 px popup/preview/Undo читаемы и доступны; RU/EN нет raw resource keys/переполнения | AppAutomation rendered PNG открытого списка и after/undo, просмотр изображений |

Новые copy cases, существующие isolation/reset/responsive tests — обязательные targeted проверки. Полные core и AppAutomation Headless suites и ordinary Desktop build уже обязательны по исходной утверждённой SPEC: новое расширение их не сокращает. Native smoke — согласно исходному плану и фактической доступности. Запуски shared-output проектов последовательные; screenshots/logs/TRX в ignored `chat-artifacts/task-tab-emoji-filters/copy/`, не коммитить по умолчанию.

План команд после approval, runner syntax принят текущим репозиторием:

```powershell
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --treenode-filter "/*/*/TaskTabEmojiFilterCopyUiTests/*" --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --maximum-parallel-tests 1 --report-trx
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --maximum-parallel-tests 1 --report-trx
git diff --check
```

Video до: не применимо к новой команде, которой раньше не было. Video после: использовать recorder harness при наличии поддержки безопасного rendered headless run; иначе подтвердить отсутствие поддержки точной проверкой, сохранить trace + просмотренные PNG. Отсутствие video не заменяет assertions пользовательского сценария. При test failure выяснить причину, исправить in-scope defect и повторить affected checks; после timeout не повторять тот же запуск без progress/root-cause evidence. Готовность не объявлять без всех обязательных AC.

## Quality gate и review

- Post-SPEC: **PASS для подготовки, не для реализации**. Компактный pass по пяти проверкам: текущий source и wrapper прочитаны; согласованный outcome/границы восстановлены; проверены empty source, master flags, independent mutation и Undo overwrite counterexamples; AC связаны с UI results/PNG/lifecycle. Два MEDIUM design findings исправлены в контракте и повторно проверены на прочитанных интерфейсе и panel layout. Числовой rubric не применяется к short SPEC.
- Отдельная предыдущая работа по изоляции не объявляется завершённой этим review; её post-EXEC и обязательные проверки остаются самостоятельным долгом.
- Post-EXEC: **PASS, локальная реализация завершена**. Новые rendered cases изолированы свежим процессом по stable test UID, как существующий importance-rendered pattern; каждый child TRX должен содержать ровно одну успешно выполненную исходную проверку. Окончательный targeted: 28/28 (Copy 16, Isolation 12), 0 failed/skipped, 6m26.2s. UI non-emoji case подтверждает сохранение непустого поиска, From/To/IsCustom/status. Existing CLI Headless harness исправлен только для его asynchronous dispatcher lifecycle; 5/5, включая worker/UI Post contract. Ordinary Desktop build 0 errors / 11 warnings; full AppAutomation Headless 58/58; окончательный **full core 1427/1427**, 0 failed/skipped/aborted, 42m52.6s; native navigation smoke 1/1. Первый полный core: 1417 passed / 10 failed; второй остановлен как superseded после 1340 passed / 7 failed в промежуточном логе, не PASS. Оба failed evidence сохранены. Scope/контракт/данные задач повторно сверены; unrelated product changes отсутствуют.

Post-EXEC review основного исполнителя (не независимый, без делегирования): все пять содержательных проверок PASS. Outcome/Non-Goals соблюдены; exact approval ограничивает действие реализацией без Git delivery; AC1–7 связаны с реальными UI/results/PNG и полными regression reports; mutable sharing/All/empty/Undo/reconnect риски проверены negative cases; findings закрыты и повторно проверены. Source/panels/подписки освобождаются владельцем. Два тестовых runtime defects (CLI dispatcher и mixed-session rendered input) исправлены адаптерами без замены пользовательских действий прямыми VM commands. Остаток: native тест проверял только навигацию, не весь copy flow; PNG не доказывают visual parity всего приложения, video не создавался. Отдельный старый риск двойного сдвига non-UTC timestamp в date-only filter отмечен в журнале и не исправлялся вне scope. Stop: можно передать результат; commit/push/PR/merge/CI/release не выполнены.

## Проверка AC на окончательном коде

| AC | Фактическая проверка |
| --- | --- |
| AC1–AC3 | `EveryTab_CopiesBothSets_ChangesResults_AndUndoesLocally` во всех девяти вкладках: реальные pointer/keyboard действия, результаты по task IDs, последующая независимая правка источника. |
| AC4 | Empty/identical/repeated copy и All master cases; отдельный `CopyUsesLatestSourceSelection_AndPreservesSearchDatesAndStatuses` сначала требует непустой исходный список, проверяет новый список и Undo при неизменных search/From/To/IsCustom/status. |
| AC5 | Реальная кнопка Undo, feedback с названием источника, повторное копирование и переключение вкладок. |
| AC6 | Ручная правка/reset/removal/reconnect; видимый открытый picker отключается при disconnect, stale вызов не меняет состояние; rename/add сохраняют допустимый Undo. |
| AC7 | RU 390 и EN 900 rendered UI cases, AppAutomation capture при 900/390; источники, feedback и Undo просмотрены в PNG. Финальные снимки: `chat-artifacts/task-tab-emoji-filters/appautomation-final/a6199ce57209406d9fd6eb670be16601/`. |

Targeted Copy 16/16 и Isolation 12/12 на окончательном rendered adapter: объединённый отчёт `rendered-isolated-targeted-v2`, ровно 28/28, 0 failed/skipped. Обычная Desktop build: 0 errors / 11 warnings. Финальный full AppAutomation Headless: 58/58, 0 failed/skipped; native smoke проверил только nine-tab/settings navigation (1/1), не полный copy flow. Последний обязательный gate закрыт: core 1427/1427, 0 failed/skipped/aborted, TRX `chat-artifacts/task-tab-emoji-filters/full-core-rendered-isolated/Kibnet_DESKTOP-AUDO1TJ_2026-10-09_20_09_53.1106994.trx`, counters и per-class results проверены напрямую. Не утверждается native visual parity или наличие video: фактический rendered runtime предоставляет `CaptureScreenshot`, evidence — PNG + UI assertions + logs/TRX.

## Approval

Получено **«Спеку подтверждаю»**, 2026-10-09. Фаза EXEC: разрешены реализация и проверки этой SPEC. Commit/push/PR/merge не поручены.

## Журнал действий агента

### Поставка ветки, 2026-10-12

Поручено «Отребейзь на мейн и оформи ветку», отдельно подтверждено «Также push и PR». Разрешены локальные коммиты, rebase на fetched `origin/main`, публикация этой feature-ветки и PR в `Kibnet/Unlimotion`; merge/release не поручены. До переноса HEAD `e987023d`, fetched main `fa1bfc03` (11 новых коммитов). Старые 1427/1427 core и 58/58 Headless — evidence до rebase, не текущая проверка нового base. План: сохранить исходный diff коммитами, сверить пересечения с новыми main Headless fixes, сохранить новые Git/settings flows, повторить ordinary Desktop build и полные core/Headless suites. Артефакты остаются ignored; screenshots/video fallback в описании PR помечаются local-only. Main в основном checkout и другие worktrees не изменять.

| Фаза / событие | Решение | Evidence / остаток | Следующий шаг | Фактическое решение человека, если требовалось |
| --- | --- | --- | --- | --- |
| SPEC, 2026-10-09 | Однократное копирование include/exclude через существующую панель, локальный Undo; прежняя изоляция остаётся | AS-IS source/panels/localization/wrapper inspected; новый source code не менялся; review выше | Получить exact approval этого расширения | UX согласован: «Да сделай»; exact approval ожидается |
| EXEC, 2026-10-09 | Подтверждённый scope, один исполнитель | Проверки и rendered evidence ещё впереди | Реализовать и проверить исходный сценарий | «Спеку подтверждаю» |
| EXEC, review / проверки | Реализованы 9 copy panels, snapshot и local Undo; поле каталога и модель задач не сериализуются | Первый targeted UI run 15/15; AppAutomation 1/1 после исправления вложенного HeadlessRuntime.Dispatch в адаптере. Снимки RU 900/390 просмотрены. До исправления адаптера честный timeout 3 минуты, не product PASS | Полные suites и native smoke | Новых решений не требуется |
| EXEC, lifecycle review | CanCopy отключён у отсоединённых panels, CopyFrom проверяет оба attached owners | Добавлен UI/negative check: открытый picker при ClearTaskSpaceSurface, прямой stale вызов не создаёт Undo; после Connect доступность восстанавливается. Предыдущий полный run остановлен только в своём проверенном process tree как устаревший после этого source fix; новый full run с Detailed progress | Получить green обязательного набора на окончательном коде | Внутренняя защита утверждённого lifecycle контракта |
| EXEC, полный core / исправление тестового ввода | В полном запуске появились падения новых pointer cases; keyboard variants проходят. До клика тест теперь ждёт две стабильные позиции и реальный hit на кнопке, как существующий WaitForHistoryWheelTargetAsync. Lifecycle case проверяет видимую панель, а не закрытый popup с неактивной binding subscription | Первый full run ещё не завершён; падения не скрыты. Product source не изменён этой корректировкой; commands не вызываются вместо реального ввода, ожидания результатов не ослаблены | Довести исходный full run до отчёта; повторить новые cases, затем обязательный набор | Новых решений не требуется |
| EXEC, full результаты / baseline harness deadlock | Первый core завершён: 1417 passed, 10 failed (9 copy и 1 hierarchy pointer isolation), 0 skipped, всего 1427; все 1399 существующих cases прошли. Full Headless завис в существующем CliLiveRefreshHeadlessTests.HeadlessSynchronizationContext.Post на вложенном Dispatch; подтверждено Mini dump и clrstack, остановлен только проверенный собственный процесс 304932 | Завершённый Headless runner отчёт: 0 completed tests / exit -1 после остановки, не PASS. Минимальная правка только тестового Post: Dispatcher.UIThread.Post вместо синхронного Dispatch; добавлена проверка deferred callback на UI thread. Production CLI/storage не меняются | Последовательный rerun Copy 16, Isolation 12, CliLiveRefresh; затем полные suites и native smoke | Проверка и исправление тестового адаптера в обязательном validation scope |
| EXEC, targeted rerun / fixture date | После ожидания input scene и видимой lifecycle панели Copy 15/16; остался дополнительный non-emoji case. Diagnostic before/after: include/exclude применены правильно, search и обе даты не изменены, но список пуст ещё до копирования. Fixture DateTimeOffset.Now (+03) после 21:00 смещается существующей UTC-date проекцией на следующий день | Новые synthetic fixtures приведены к UtcNow; non-emoji test сначала требует оба исходных результата, после copy/Undo проверяет их и сохранение From/To/IsCustom/status/search. Дефект исходного production date filter для non-UTC values не исправляется вне scope; прежнее обобщение всех failures как pointer уточнено этим finding | Повторные 16/12 cases, исправленный CLI adapter, full suites/native | Утверждённые проверки, без изменения product dates |
| EXEC, targeted / Headless lifecycle | Copy 16/16, Isolation 12/12. Асинхронный Post устранил nested Dispatch deadlock, но CLI cases пока timeout: обычная PerTest изоляция сбрасывает dispatcher между Dispatch calls, worker callback не доходит. Новый negative contract покрывает Post и с UI, и с worker thread | CLI rerun v1 1/5, v2 0/5 — не PASS. Existing Recovery session hook расширен только на CliLiveRefreshHeadlessTests: PerAssembly dispatcher на время одного case, restore default после каждого; Post использует captured UI dispatcher. Product CLI/storage не изменены | Проверить все 5 CLI cases, затем обязательные full/native gates | Test-harness correction для честного полного запуска |
| EXEC, подтверждённые targeted/native gates | Copy 16/16; Isolation 12/12; CLI Headless v3 5/5, 29.7s; native nine-tab/settings navigation smoke 1/1, 37.8s. RU 390 source/feedback и EN 900 feedback PNG повторно просмотрены; capture доказывает меню, не полную visual parity приложения | Новые tests не удалены/не ослаблены; хранилище/поиск/даты не переписаны. Начат последовательный окончательный pipeline: Desktop build → full Headless → full core | Получить оба полных зелёных отчёта, закончить reviews и handoff без Git delivery | Новых решений не требуется |
| EXEC, повторный full / rendered isolation | Desktop 0 errors / 11 warnings; full Headless 58/58. Повторный core обнаружил 7 pointer failures новых copy cases при 1340 passed на момент остановки; корень hit-test сцены не содержит popup button. Этот запуск не PASS и остановлен только в проверенном собственном process 647968 как устаревший после test-adapter change | Как существующие ImportanceRenderedProcess tests, новые 28 rendered cases запускаются каждый в свежем процессе по stable test-node UID; дочерний TRX должен содержать ровно 1 passed case. Input root определяется после рендера, настоящий mouse input/assertions сохраняются. Product source не менялся | Проверить все 28 cases и повторить полный core на окончательном adapter | Новых решений не требуется |
| EXEC, окончательный targeted / full / handoff | Первый объединённый selector выбрал 0 tests, minimum-expected policy вернула exit 8 — не PASS. Исправленный wildcard selector реально выполнил все 28 cases, 28/28, 6m26.2s. Полный core 1427/1427, 42m52.6s; full Headless 58/58; build 0 errors / 11 warnings; native navigation 1/1 | Все AC закрыты в заявленной evidence границе; прежние responsive 22/22, reset 10/10, affinity 14/14, StormEmoji/StormReset по 1/1. Новый core RU390 feedback и AppAutomation wide source PNG повторно просмотрены. Failed/aborted runs сохранены в ignored artifacts; финальный diff check PASS | Передать локальную реализацию без commit/push | Новых решений не требуется |
