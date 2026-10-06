# Доставка emoji-проекции на UI-потоке без повторного входа

## 0. Метаданные
- Текущая фаза: EXEC, 06.10.2026; пользователь подтвердил именно эту SPEC фразой «Спеку подтверждаю». Outcome один, риск medium: общий reactive pipeline, межпоточная доставка и синхронная initial projection. Использована компактная expanded форма центрального `templates/specs/_template.md`; short eligibility не выполнена из-за существенной межкомпонентной неопределённости producer. Предшествующие SPEC-only записи ниже — история подготовки.
- Профили: `dotnet-desktop-client`, `ui-automation-testing`; context `testing-dotnet`. Behavior baseline каталога GPT-6 Astra, поверхность Codex desktop; model eval не применим к .NET/Avalonia изменению.
- Один предполагаемый владелец EXEC — этот emoji-чат после отдельного approval. Автор CLI scheduler не меняет; других writers в перечисленных файлах быть не должно.
- Проверенный локальный main: `68197565816295c7c766877c39cc40a72b3aeeb4`. Read-only GitHub preflight: PR317 MERGED, remote main `dce4e1961b3f29e33a2e439730cd09cac885a872`. Checkout/fetch/rebase на SPEC не выполнялись; EXEC начинается с проверки актуального main и изолированной рабочей ветки.
- Разрешён текущий результат EXEC: локальные product/test changes в §16, targeted/full Main/Headless и desktop build, просмотр UI evidence. Push/PR/merge/installation и личный task space не входят в это approval. Записи SPEC-only ниже сохраняются как история до подтверждения.

## 1. Overview / Цель
При переключении «Все» в emoji-фильтре во время upstream refresh дерево должно обновляться без исключения вставки контейнера и без повреждения состава/выделения. Success: уведомления `CurrentAllTasksItems` доставляются последовательно на UI-потоке; первоначальная проекция и обычные UI-origin updates остаются синхронными. Output EXEC: минимальное исправление подтверждённой границы доставки, различающие regression tests и просмотренные UI/evidence. До approval output — только эта SPEC.

## 2. AS-IS и evidence
- Сохранённый PR316 run37234435561/headb4c8839e до CLI: trace `emoji-pr316-attempt1/test-results/37234435561-1/main/diagnostics-8704.jsonl`, SHA256 `64D1001B8A243AE26EAEFDFA5D4F5BDFACC19A2B0CD9D2FC81A25C198F5DFF51`. В одном False case/testExecutionId: seq614 Reset(UI22) →615 Add0(offUI10) →616 Add1(UI22). Hash и исходные строки проверены.
- PR317 run37459345100 attempt1/head7d3e4d6a: тот же порядок seq768/769/770, UI13/offUI22/UI13; единственный FAIL `Toolbar_EmojiFilters_AllToggle_WithReentrantSourceUpdate_KeepsCollectionConsistent(False)`, main1348/1349, Headless55/55. Stack: `AvaloniaList.Insert` → `PanelContainerGenerator` → `SortedObservableCollectionAdaptor`; main VM621, control493/339, test268. TRX/trace сверены по manifest.
- Архивы: `chat-artifacts/coordination/cli-ci-green-7d3e4d6a-attempt2/` (старый trace, comparison, disposition/review) и `chat-artifacts/coordination/cli-ci-failure-7d3e4d6a-run37459345100/` (новый trace/TRX, manifest SHA `E35E996EC1A7B0ADD5635D8407C159522CEA77EDB09833F7313BB75533B64823`). Старый/новый UI-defect подтверждён; producer, перекрытие уведомлений и вероятность падения ещё не установлены.
- В `MainWindowViewModel`: All-handler обходит `EmojiFilters.ToArray()` (защищает перечисление), `emojiRootFilter` использует `Throttle(600ms)` без явного scheduler, корневой bind — `ObserveOn(CurrentThreadScheduler.Instance)`. Current-thread trampoline не переносит события на UI. `searchTopFilter` отдельно задаёт UI scheduler. Search throttle — кандидат, не доказанная причина; `SealPendingSaves` не закрывает эти timers.
- Immutable retry PR317: main1349/1349, Headless55/55, 0 skipped по сохранённому `verified-gate.json`. Это разрешило отдельную CLI-доставку с раскрытием существующего UI-defect, а не доказало его исправление.

## 3. Проблема и границы старого approval
Нарушен UI-affinity контракт привязанной корневой коллекции; сохраняется риск неправильного порядка container notifications. Старая `2026-10-02-emoji-title-refresh.md` покрывала Unicode recognition, Title→filter/child updates и UI coverage. §6.1 требовал failing evidence для изменения production-подписок, §16 не планировал их изменения. Новый факт выполняет условие диагностики, но не переносит старое approval на отдельный scheduler/serialization contract. Нужна новая точная фраза «Спеку подтверждаю» для этой SPEC; старые approval, PR315 и принятие CLI не являются её approval.

## 4. Цели дизайна
Один UI-owner связанной root projection; ordered notifications; синхронный первоначальный результат и UI-origin refresh; bounded tracing без private data; отсутствие глобальной замены scheduler и лишних dispatcher hops.

## 5. Non-Goals
Распознаватель/corpus/шрифт, семантика include/exclude/«Все», граф отношений/статусы/хранилище/CLI, глобальные ReactiveUI schedulers и package upgrades, новый layout и установка приложения. Не скрывать ошибку sleeps, отключением injection/assertions, catch-and-continue или принудительным успешным retry.

## 6. TO-BE
### 6.1 Алгоритм и ownership
1. На закреплённом baseline добавить test-scoped probe в существующий AllToggle: notification begin/end, глобальную и per-thread depth, thread/UI access, Action/indices/count и snapshot item IDs; first-offUI stack ровно один раз на collection/test. Begin/end обрамляют всю доставку CollectionChanged всем подписчикам (например, test seam notifying collection), а не только тело первого test-handler: иначе поздний ItemsControl consumer не проверен. Записать вход producer и выход перед bind. На RED baseline owner-thread сам по себе не защищает snapshot от worker writes: live IDs/count читать только после явного producer quiescence или согласованного version checkpoint. До него — frozen event payload/IDs, stack и последний immutable snapshot с собственной версией. Поздний async snapshot не выдавать за состояние конкретного Reset; неизвестный snapshot явно помечать. Probe снимается в finally вместе с fixture; данные синтетические.
2. Различить одинаковый refresh на UI и worker через управляемый scheduler/барьер; добавить mixed case: worker batch уже принят boundary и ждёт UI → обычный UI refresh. Сначала проверить `emojiRootFilter` timer; если он не объясняет offUI event, локализовать реальный producer с source identity. Все barrier waits асинхронны вне notify, с конечным budget/cancellation и finally-release; запрещены sync Dispatcher.Invoke и ожидания из CollectionChanged/UI blocking. Никаких случайных Delay как условия воспроизведения.
3. Исправить минимальную доказанную границу: UI scheduler найденного delayed producer, если достаточен контракт; иначе instance-scoped root UI-affinity/serialization boundary. Linearization point — принятие sorted batch перед root bind; FIFO по этому порядку, без обгона queued worker batch UI fast path. Обычный direct UI refresh при depth0 должен синхронно завершить более ранние принятые batches и собственный batch до возврата, без дополнительного dispatcher turn; initial bind сохраняет synchronous result. Nested updates при depth>0 доставляются после завершения текущего уведомления. Blind all-async ObserveOn(MainThread) запрещён без этих characterization checks. Механизм выбирает владелец по RED в write set; иной producer/storage/global fix требует нового scope gate.
4. Сохранить исходный injection `sourceTask.Title` из notification handler, порядок регистрации handler перед view и все существующие assertions обоих False/True cases. Probe не мутирует bound collection напрямую.

### 6.2 Пользовательский сценарий, state и evidence
| Сценарий / состояние | Trigger | Ожидаемый видимый результат | Evidence / AC |
| --- | --- | --- | --- |
| Include/exclude popup открыт, «Все» выбран | Space + upstream regroup/refresh | Popup остаётся открыт, surviving flags переключены, retired ❌ group удалена, selection сохранён, tree IDs/контейнеры согласованы | Actual control/input assertions; before/after frame; AC1–3 |
| Initial / обычное UI-origin обновление | Initial bind готового source либо direct UI filter/source refresh | Initial collection готова сразу после await Connect, direct refresh — до возврата; без дополнительного dispatcher turn | Synchronous characterization; AC2 |
| Refresh приходит с worker / nested notification | Управляемый producer | После доставки все root notifications на UI, без вложенного consumer notification; второй Space/Escape работает | Scheduler-controlled trace + actual containers; AC1–3 |
| Старый worker batch уже принят и ждёт UI | Следом обычный direct UI refresh | FIFO старый→новый; UI call возвращается с актуальной проекцией без extra pump, чужой batch не обойдён | Mixed controlled case, acceptance/publication IDs и order; AC1/2 |
| Empty / delete / close | Удаление последнего matching item либо disposal с pending callback | Согласованный пустой результат, нет late publications в закрытый fixture | AC2/3; lifecycle assertions |

Visual planning: layout не меняется; эталон — существующий normal popup/tree до toggle. На EXEC сравнить before/after одинакового synthetic fixture: flags, selected All, отсутствие retired group, actual visible tree и контейнеры. Video fallback для Avalonia.Headless без native HWND: input/control assertions, просмотр paired PNG и trace; запись native не входит в scope. Producer race доказывается trace, не снимком.

### 6.3 Decision Ledger / runtime contract
| Решение | Владелец | Выбранный вариант / риск | Нужно до EXEC |
| --- | --- | --- | --- |
| Новое approval | Пользователь | Эта SPEC, а не старое Unicode/CLI approval | Да: exact phrase |
| Механизм доставки | Emoji-владелец | Минимальный producer или root boundary по RED; риск async initial / повторного входа проверяется явно | Нет: engineering choice внутри scope |
| Ownership | Этот чат | Единственный writer MainWindowViewModel и relevant tests; CLI не правит scheduler | Подтверждение SPEC закрепляет scope; conflicts останавливают writers |
Runtime: baseline/candidate pinned SHA, SDK/packages/config одинаковы; normal desktop и test commands без специального production flag. Persisted/config/API contracts не меняются, migration не нужна.

## 7. Инварианты
На final candidate все root CollectionChanged имеют UI access=true, единый owner и complete consumer depth≤1; begin/end сбалансированы. Принятые sorted batches доставляются FIFO; depth0 UI fast path не обгоняет ранее queued worker batch. Индексы сверяются replay-моделью по frozen payload и достоверным versioned/quiescent checkpoints. Reset snapshot на RED может быть unknown; owner-thread без quiescence не считается доказательством его точного состояния. Итог после quiescence: IDs/order совпадают с reference projection; realized UI containers соответствуют ожидаемым видимым IDs/позициям (не требовать всех виртуализированных offscreen items). Старый согласованный state не принимается за PASS.
End фиксируется в finally даже при ошибке consumer; исходный exception не подавляется. Ошибка telemetry сообщает failure после cleanup через существующий trace contract, не прерывая освобождение session. Pending callbacks отменяются/отсоединяются при disposal; новую общую обработку ошибок приложения не вводить.

## 8. Интеграция / триггеры
All-filter ShowTasks, task regroup и search refresh → root filter/sort/bind → ItemsControl consumer. Проверить источник перед bind и notification boundary после него; соседние emoji filter collections диагностировать при локализации, но не вводить универсальный UI threading refactor.

## 9. Модель данных
Не меняется. Queue/probe, если нужны, instance-scoped/disposable; persisted поля и private task space не используются.

## 10. Rollout / rollback
Локальная candidate ветка после approval; baseline evidence сохраняется. Rollback — revert только UI-affinity fix и его test seam; сохраняются Unicode fix и CLI. Push/PR/merge/release/installation требуют отдельного поручения, сейчас не входят в результат.

## 11. AC → обязательные проверки
| AC | Готовый результат | Проверка / evidence |
| --- | --- | --- |
| AC1 | UI-owner, whole-notify serialization и FIFO соблюдены | UI/worker, nested и mixed queued-worker→UI cases: deterministic RED старой границы по affinity/order, GREEN fix; first-offUI origin локализован либо непокрытый producer блокирует completion. Accepted/publication batch IDs, begin/end/depth, frozen payload и versioned/quiescent checkpoints, valid indices/realized containers; no fixed sleep |
| AC2 | Initial bind готового source и depth0 direct UI refresh синхронны; empty/disposal безопасны | Initial сразу после await Connect; direct UI call при empty queue и при pending older worker batch — оба до возврата без RunJobs/Delay, FIFO old→new. Не требовать синхронной загрузки storage; nested callback ждёт outer end; disposal не допускает late publication. Barriers bounded/cancellable, finally-release |
| AC3 | Исходный AllToggle/injection/assertions, popup/selection/tree и emoji Title flow сохранены | False/True без ослабления; root IDs и actual tree; second Space/Escape; точная 🧙‍♂️→🪼 Title regression и ребёнок/внук; paired PNG просмотрены при одном размере |
| AC4 | Исправлен тот же воспроизводимый контракт; исходники/evidence не смешаны | Один baseline и один final candidate SHA, одинаковое окружение; pinned probes и full summaries различены. Для sporadic exception deterministic offUI RED допустим; один green retry не доказывает fix |
| AC5 | Оба обязательных проекта и стандартная desktop build проходят последнего source | Fresh unfiltered Main и Headless, 0 failed/skipped, exit0; desktop build; source hash identity; expanded post-EXEC review и visual acceptance |

Команды EXEC (TUnit filters используют `--treenode-filter`): targeted `/*/*/*/*AllToggle_WithReentrantSourceUpdate*`, `/*/*/*/*WizardToJellyfish*` и новые affinity cases; затем `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --maximum-parallel-tests 1 --output Detailed` и `dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -- --maximum-parallel-tests 1 --output Detailed`; обычный `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj`. До EXEC ничего из этого не запущено. Базовые замеры производительности не цель; queue должна быть bounded/disposable, исходный sync контракт проверяется AC2.

## 12. Риски / ожидаемые замечания
| Возражение / риск | Решение и проверка |
| --- | --- |
| «CLI уже зелёный, значит исправлено» | Старый и новый raw offUI trace проверены; retry исторический, этот UI-defect открыт |
| «UI dispatcher сделает первый результат асинхронным» | AC2 проверяется до возврата, не после pump; broad replacement не предрешён |
| «Уберите callback/sleep и тест пройдёт» | Injection, handler registration и исходные assertions сохраняются; controlled scheduling вместо timing workaround |
| «CurrentThread или lock достаточно» | UI access и consumer depth/IDs — отдельные invariants; deadlock/blocking waits запрещены |
| «Предыдущая спека уже принята» | Старый write set не включал production subscriptions; это отдельный scope и новое approval |

## 13. План EXEC после approval
Preflight актуального main/ownership → scoped branch и pinned baseline → probes/UI-worker RED + producer → минимальный fix → targeted/UI frames → full Main/Headless/desktop → final hash/visual/user-observable review. Ошибка обязательного теста блокирует completion; повтор только после новой диагностической гипотезы/устранения конкретного failure. На SPEC — только документ/review.

## 14. Открытые вопросы
User-owned продуктовых решений кроме approval нет. Producer и точный instance scheduler — engineering investigation внутри утверждаемого scope; неизвестный producer не объявляется установленным. Если локализация требует изменения CLI/storage/global schedulers, остановить dependent work и уточнить scope.

## 15. Соответствие профилю
Actual Avalonia UI tests обязательны по AGENTS.override. Применимые appautomation/TUnit правила: real control flow, first RED, review кадров, pinned source и раздельные full outcomes. SPEC не разрешает текущие full/native runs.

## 16. Планируемый write set
| Файл | Ответственность / предел |
| --- | --- |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Найденный emoji producer/root delivery boundary, initial sync/instance disposal; остальные команды не менять |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | Существующий AllToggle плюс controlled UI/worker/lifecycle и actual UI assertions |
| Новый `src/Unlimotion.Test/EmojiProjectionUiAffinityTests.cs` при необходимости | Минимальная characterization/replay/initial-sync проверка |
| `src/Unlimotion.Test/TestExecutionTrace.cs` либо test-local probe | Только bounded first-offUI stack и notification begin/end/IDs; formatter/general scheduler не рефакторить |
| Эта SPEC | Evidence, findings, phase/approval и журнал |
Product control, parser, CLI, storage, package/build scripts вне write set; при необходимости — scope gate, а не скрытая правка.

## 17. Было → стало
Mixed UI/worker root notifications → один UI-owner и ordered delivery; потенциальный container Insert failure → согласованная проекция при том же refresh. Initial synchronous behavior и filter semantics → сохранены.

## 18. Альтернативы
Producer-specific UI scheduling меньше меняет contract, но допустимо только после локализации и проверки остальных входов. Root affinity boundary шире покрывает входы, но требует explicit synchronous/serialization/lifecycle checks. Retry/sleep/подавление исключения — не исправление. Глобальная scheduler замена — вне scope.

## 19. Quality gate / review SPEC (история до approval)
Ниже — проверка готовности SPEC, не runtime PASS. Post-EXEC и AC1–5 не выполнены; code/tests/builds/full/native/Git delivery не запускались. Отдельный reviewer выполнил Scope/Evidence, Contract/Adversarial и Role passes, затем адресный fresh re-review §6/7/11/19: Contract PASS, оба MEDIUM закрыты, новых actionable findings для фазы SPEC нет.

### Linter / rubric
| № | Проверка | Итог и основание |
| --- | --- | --- |
| 1 | Outcome | PASS: §1/6, обычный Space и upstream refresh |
| 2 | AS-IS | PASS: §2, old/new hashes/seq, source и PR317 read-back |
| 3 | Проблема | PASS: offUI notification доказан; producer не заявлен найденным |
| 4 | Дизайн | PASS: UI-owner, order, initial sync и bounded diagnostics |
| 5 | Границы | PASS: §3/5/16, новая SPEC отдельно от old approval |
| 6 | Ответственности | PASS: один будущий writer, CLI исключён |
| 7 | Интеграция | PASS: §8, producer/pre-bind/consumer |
| 8 | Инварианты | PASS: §7, complete notify depth/replay/IDs |
| 9 | Ошибки/recovery | PASS: original throw/finally/disposal/telemetry cleanup |
| 10 | Performance | PASS: redesign/benchmark не цель; bounded instance queue/no extra UI hops, sync AC2 |
| 11 | Данные/state | PASS: §9, ничего persisted |
| 12 | Совместимость | PASS: filter semantics/initial sync, без migration |
| 13 | Rollback | PASS: §10, scoped revert сохраняет CLI/parser |
| 14 | AC | PASS: AC1–5 измеримы |
| 15 | AC/evidence | PASS: §11, RED/GREEN, UI/worker, disposal, actual containers/PNG/full |
| 16 | Команды/stop | PASS: §11/13, TUnit syntax и source identity |
| 17 | План | PASS: diagnostic RED до выбора fix и mandatory full |
| 18 | Решения | PASS: §6.3/14, неизвестный producer — engineering stage, approval отдельно |
| 19 | Форма | PASS: compact expanded из-за concurrency uncertainty |
| 20 | Профиль | PASS: §15, UI tests/visual review обязательны на EXEC |

Rubric: цель/границы5, AS-IS5, конкретность дизайна2 (producer ещё неизвестен; вариант выбирается по RED в bounded write set), безопасность/rollback5, проверяемость5, автономность5 (user-owned вопрос только approval; выход за write set остановлен). Итого27/30; оценка не заменяет phase/validation gates.

### Role-Based Review Result
| Роль | Прикладная проверка | Итог SPEC |
| --- | --- | --- |
| Domain workflow | Flags/All/selection/relationships сохранены; prior occurrence отделён от CLI acceptance | PASS |
| UX / designer | Popup/tree и исходный input проверяются controls + paired PNG; layout redesign не нужен | PASS |
| Tester | Controlled UI/worker, notify целиком, first-offUI stack, old injection/assertions, pinned source/full | PASS |
| Developer / architect | Локализовать producer; initial sync, reentrancy, disposal, отсутствие global replacement | PASS |
| Delivery / security | SPEC-only до exact approval; synthetic evidence; scoped rollback; Git delivery отдельно | PASS |

### Post-SPEC review passes (root)
- Scope/Evidence: прочитаны эта/старая SPEC§6.1/16, central quest governance/mode/template/linter/rubric/review owners, MainWindowViewModel root/search/All bind, control toggle, existing AllToggle и trace writer; сравнение/manifest/старый raw trace SHA/seq614–616, новый trace/TRX и PR317 MERGED/remote main read-back. Git status содержит только новую SPEC; checkout main681 не менялся.
- Contract: old approval не переносится; pipeline/data/parser/CLI границы и AC/evidence заданы. Синхронность storage Connect не обещана: initial collection проверяется сразу после await Connect. До runtime проверок никакой исправленный UI-defect не объявляется.
- Adversarial: first-handler begin/end не доказывал бы complete notify; unsafe offUI enumeration мог бы исказить trace; sleep/ослабление injection скрыли бы дефект; all-async scheduler ломал бы initial. Контрпримеры закрыты текстом§6/7/11, queue/disposal входят в characterization. Прошедший immutable retry и старый equal source не заменяют fix evidence.
- Fix/re-review: self-review уточнил whole-notify envelope, initial-vs-storage и finally/error contract. Reviewer нашёл два MEDIUM: unsafe RED owner snapshot и пропущенный queued-worker→UI fast-path case. Исправлены frozen/versioned/quiescent snapshot rules, явный FIFO/linearization point и mixed synchronous characterization; запрещены sync Invoke/blocking notify waits, waits bounded/cancellable с finally-release. Адресный fresh re-review §6/7/11/19 подтвердил закрытие обоих замечаний.
- Depth: outcome/AS-IS, old/new approval scope, ownership, producer uncertainty/alternatives, reentrancy/thread affinity, initial sync, lifecycle/errors, data/Git boundaries, UI rendering/actual input, AC mapping/commands/stop и реальные hashes проверены. Изменений кода или запусков нет.

### Findings disposition / Stop decision
| Finding | Severity / источник | Исправление и итог |
| --- | --- | --- |
| Первый handler не обрамляет всю доставку; initial bind нельзя смешивать с синхронной загрузкой storage | MEDIUM / self-review | Whole-multicast envelope и immediately-after-await-Connect checks заданы в §6/11; закрыто |
| Owner-thread snapshot на RED может пересечься с worker write | MEDIUM / reviewer | Quiescence/version checkpoint, frozen payload и immutable snapshot/version; поздний snapshot явно диагностический. Fresh re-review: закрыто |
| UI fast path может обойти ранее принятый worker batch | MEDIUM / reviewer | Linearization/FIFO, mixed controlled case и sync-before-return при pending batch; bounded/cancellable gates вне notify с finally-release. Fresh re-review: закрыто |

No-findings justification после fixes: reviewer и root проверили каждый текущий SPEC gate, scope/approval, факты trace против заявлений, полную notification границу, RED snapshot safety, UI/worker/mixed/nested порядок, initial sync и disposal, UI evidence и обязательную будущую валидацию. Неизвестный producer и открытый runtime defect явно остаются этапом EXEC и не объявлены устранёнными.

Sandbox limitation: reviewer действовал read-only, но effective sandbox `danger-full-access` разрешает запись; технически независимая read-only изоляция недоступна. Поэтому отдельный reviewer не считается технически изолированным; root выполнил fallback Contract/Adversarial/Role проверки и fresh fix review. Файлы reviewer не менял, runtime не запускал.

Stop decision: **PASS-SPEC — документ готов для нового решения пользователя; EXEC не разрешён**. Открытых actionable замечаний к текущей фазе нет. Runtime AC1–5 и post-EXEC review остаются обязательными после approval; этот PASS не разрешает push/merge.

## Approval
Новая фраза пользователя «Спеку подтверждаю» получена 06.10.2026 именно для этой SPEC. Основание: central `instructions/core/quest-mode.md`: «Фразу пользователя `Спеку подтверждаю` считать единственным переходом из фазы `SPEC` в фазу `EXEC`». Разрешена реализация и обязательные локальные проверки. Push/merge/installation этой фразой не разрешены.

## 20. Журнал действий агента
| Фаза / событие | Решение / evidence | Остаток / следующий шаг | Решение человека |
| --- | --- | --- | --- |
| SPEC / новый факт, 06.10 | Старый hash/seq614–616 проверены, prior occurrence подтверждён; producer не установлен. PR317 MERGED/remote main проверены read-only; старый SPEC write set отличён от нового | Локальная новая SPEC; expanded post-SPEC review, затем показать готовый результат для approval | Нового approval нет; разрешены только анализ и эта SPEC |
| SPEC / review fixes и передача, 06.10 | Self-review и два MEDIUM reviewer исправлены; адресный fresh re-review PASS, disposition и writable-sandbox fallback записаны | Показать готовую SPEC; до нового approval product/test code и Git delivery не выполнять | Ожидается «Спеку подтверждаю» именно для этой SPEC |
| EXEC / approval и preflight, 06.10 | Получена точная фраза; managed WT `emoji-ui-affinity/Unlimotion`, ветка `fix/emoji-ui-affinity`, исходный main `dce4e1961b3f29e33a2e439730cd09cac885a872`, совпадает с live remote main. SDK10.0.401, TUnit1.44/MTP2.2.2 | Один writer; последовательные тесты, пользовательский Desktop21640 не трогать | SPEC подтверждена; внешняя доставка отдельно |
| EXEC / первый RED, 06.10 | `artifacts/emoji-ui-affinity/red/run.log`: UI-origin PASS, same worker-origin FAIL по `UiThread=false`; trace624324 whole notify/first-offUI stack. Producer завершён до quiescent snapshot; layout не менялся | Controlled delayed-search и mixed/pending/lifecycle characterization, затем минимальная граница root delivery | Дополнительных решений нет |

## 21. Реализация и evidence EXEC

Baseline `e6e06a5820c49ca17e1fa7b20030827f199b8271` — локальный characterization поверх main `dce4e1961b3f29e33a2e439730cd09cac885a872`. Root delivery ещё использует `CurrentThreadScheduler`. `artifacts/emoji-ui-affinity/characterization/run.log`: 6 tests, 2 PASS / 4 ожидаемых FAIL, 0 skipped. UI-origin проходит, worker-origin и controlled delayed worker подтверждают offUI; mixed FIFO и pending disposal также RED. Первый stack delayed producer содержит `Throttle.Propagate → Filter.Dynamic → CurrentThread → root bind`. Это deterministic affinity evidence; sporadic container exception повторно не заявляется.

Выбран instance-scoped root delivery boundary: изменения фильтра могут приходить непосредственно с worker и от delayed search, поэтому только замена timer scheduler не закрывает оба входа. Sorted batches принимаются под коротким lock с последовательным ID. Вызов с owner thread синхронно дренирует FIFO, включая ранее принятый worker batch; nested producer enqueue не вызывает вложенный consumer. Worker постит один drain на UI. Cancellation slot назначается до Schedule, чтобы поздний returned handle не отменял более новый post. Disposal/consumer error/Schedule rejection прекращают доставку и сохраняют исходную ошибку. Queue ограничена 4096 batches; превышение явно завершает subscription с InvalidOperationException, без потерь, coalescing и блокирования producer.

Owner ID захватывается внутри callback фактического UI scheduler, а не по наличию произвольного SynchronizationContext. Connect ожидает завершения initial bind: коллекция готова сразу после await Connect. Вызовы model без context сохраняют прежний synchronous CurrentThread path. Общие schedulers, storage, CLI, packages и controls не менялись.

Test-local probe обрамляет whole CollectionChanged multicast, включая ItemsControl; записывает begin/end, thread/global depth, frozen payload/indices и accepted/published batch IDs. Внутренние события Load под SuspendNotifications не считаются доставкой. Frozen Reset checkpoint сохраняется до disposal suspension scope; любой unknown Reset в candidate сразу проваливает проверку. Replay сверяет каждый Add/Remove/Move/Replace и итоговый ID order с quiescent snapshot. Отдельный отрицательный поток `unknown Reset → invalid Add → known Reset` требует именно AssertionException. Probe не перечисляет живую коллекцию при worker mutation.

Semantics suspension проверены по исходникам точной версии DynamicData 9.4.31: [SortedObservableCollectionAdaptor](https://raw.githubusercontent.com/reactivemarbles/DynamicData/9.4.31/src/DynamicData/Binding/SortedObservableCollectionAdaptor.cs), [ObservableCollectionExtended](https://raw.githubusercontent.com/reactivemarbles/DynamicData/9.4.31/src/DynamicData/Binding/ObservableCollectionExtended.cs), [IObservableCollection](https://raw.githubusercontent.com/reactivemarbles/DynamicData/9.4.31/src/DynamicData/Binding/IObservableCollection.cs).

Исходный AllToggle сохраняет Title injection из первого notification handler, его регистрацию перед view, все прежние assertions, второй Space и Escape. Добавлены actual root ID order/reference, realized container DataContext и paired PNG при 390×760. Popup перекрывает короткий filtered tree на after frame: белая область ниже popup не трактуется как исчезновение root items; 6 IDs и realized container подтверждены отдельно. Точные wizard→jellyfish сценарии сохраняют filter selection и детей/внуков до save.

Промежуточные неуспешные попытки не входят в финальный PASS: `targeted/run.log` — неверный filter, 0 tests; `review-fixes/run.log` — недостаточный replay checkpoint, 4 FAIL; `review-fixes-final/run.log` — wrong explicit interface, compile error. После исправлений `run-corrected.log` 17/17, `mixed-actual/run.log` 11/11. Последний `final-targeted/run.log` — **19/19, 0 failed/skipped, exit0**, после строгого Reset check; сужение catch до AssertionException проверяется последующими full gates.

Post-EXEC findings: replay gaps, Schedule rejection cleanup и context≠owner исправлены; адресный reviewer подтвердил закрытие всех MEDIUM. LOW broad exception catch в отрицательном тесте исправлен до полного прогона. Reviewer выполнял только чтения, но sandbox writable; технической read-only изоляции нет, root fallback Contract/Adversarial/Role review обязателен. Полные Main/Headless/desktop gates и финальный вердикт пока pending.

Первый pinned candidate `834c5388e01bfa6ab1bc5daf4a6c35f55173dac8` запустил unfiltered Main. Оба AllToggle False/True провалились на прежнем обязательном `Assert.That(injected).IsTrue()`: после Space не было root notification, trace before-toggle показывал все false. Full-suite Skia PNG оказались полностью black, включая прошедшие wizard tests; эти кадры не visual evidence. Shared-session/rendering первопричина не заявляется установленной. Найден конкретный harness gap: rows можно прочитать из bound Items при закрытом popup, pointer route не проверял открытие, результат list.Focus игнорировался. На двух failures gate уже неуспешен; остановлен только owned Test693208 после проверки PID/executable path, wrapper exit−1 в `full-main/result.json`. Неполный прогон не считается full PASS.

Исправлена только подготовка input в AllToggle: тот же normal PART_Input Focus+Enter route, который используют Title cases; bounded wait открытого видимого popup со ненулевым arranged list, затем focus/selected-All assertions перед arming и Space. Root product code не менялся, Title injection/registration/старые assertions/второй Space/Escape сохранены. Mouse-opening coverage остаётся в существующем toolbar normal scenario. `input-ready/run.log`: 19/19, 0 failed/skipped, exit0. Дополнительно before-toggle trace показывает popup/focus/bounds. После закрепления финального candidate требуется fresh unfiltered Main заново.

Условие окружения: во время первого full Main появился чужой SynchroPusher full PID708436/vstest701788/testhost707600; его процессы не менялись. Overlap сохранён в `full-main/foreign-process-overlap.json`, timing не используется как benchmark. Перед следующим тяжёлым запуском проверить завершение чужого full. Пользовательский Desktop21640 продолжал работать, установленная версия не менялась.

### Дополнительные findings после первого candidate

Второй unfiltered Main (`gate-main`, source `de0ca6c46ed950cb3bb1c55c002da4a32c6f1f28`) прошёл оба AllToggle и новые affinity cases, но остановлен после `TreeSearch_ClearSearch_ReselectsAndScrollsCurrentAllTasksItemWithClosedDetails` FAIL: parent expanded, replacement child wrapper/container существует и видим, VM/tree selection null. `gate-main/result.json` exit−1 и `stopped-after-failure.json` фиксируют только owned process; этот неполный запуск также не full PASS. Исходный тест без изменений повторно провалился в `oracle-selection/run.log` (4 PASS/1 FAIL), затем стал GREEN после исправления ниже.

MEDIUM oracle gap: старое `surviving filters all=true` принимало потерю дополнительных групп; `final-visual` True after действительно потерял также 🧰 Beta, без source Title/Emoji/identity trace. Точная причина того кадра не доказана. Characterization `RootProjection_SealedEditorLateDiskRefresh_ChangesTitleAndEmojiOnSameTask` на `26e423279e1abe25e598375333e420d03dbe367e` подтвердил разрешённый поздний watcher refresh после `SealPendingSaves`: тот же Root3 object переходит с синтетического 🧰 Title на disk `Task 3`, Emoji становится пустым, группа исчезает. Это fixture counterexample, не доказательство происхождения старого PNG. Controlled AllToggle теперь использует существующий factory override с `watcher:false`, сохраняет живые source PropertyChanged/filter/control subscriptions и тот же first-notify injection. До/после проверяются все 7 exact Title/Emoji/same object identities, before8/after7 exact group keys, include6 exact source IDs и исключение шести root IDs в exclude. Старые assertions, before-view registration, второй Space и Escape сохранены; normal save/reload BDD остаётся с настоящим storage/watcher.

Selection characterization первой версии на `26e42327` ошибочно принимала Any(child) до завершения searchTop stage, поэтому её PASS не объяснял исходный failure. На test-only `6390b1ee0cf4233b530479c730b9799a2bc4d69c` исправлен setup: root Count==1 с точным child ID до выбора; после очистки UI stage добавляет parents, старый promoted child ещё root, оба legacy retries выполняются до управляемого worker stage. `search-two-stage-red`: 0/1, exit2. Trace seq99 показывает VM/tree на старом searchWrapper; seq106 после позднего удаления child — оба null, хотя новый nested wrapper существует. Это конкретный preservation counterexample; совпадение causal order со старым full trace без notification instrumentation не заявляется.

Минимальная коррекция внутри root completion: restore после clear не считается окончательным, пока при выключенных emoji-фильтрах выбран parented task из direct root search projection. Pending restore сохраняется до следующего root batch, где выбирается новый nested wrapper. Произвольные delays и existing TreeSearch assertions не изменены, storage/children pipeline не правились. `search-two-stage-green/run.log`: управляемый case и неизменённый исходный UI test 2/2, 0 skipped, exit0. Затем усилены setup/phase selection assertions; final targeted/full должны пройти уже последнюю версию.

`oracle-selection` кадры оказались black после смешанного App/Skia запуска и не входят в visual acceptance. Финальные paired frames выполняются отдельным fresh process только для Skia Title/AllToggle, с source identity manifest. Полные gates и final expanded review пока pending.
