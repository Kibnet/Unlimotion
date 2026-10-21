# Ускорение исполнения полного набора тестов

## 0. Метаданные
- Статус: EXEC завершён локально 2026-10-21; результаты и границы проверки — §11/19.
- Форма: Expanded, medium: меняются CI orchestration и межпроцессная изоляция, поэтому short неприменима.
- Владелец: Codex; переход SPEC → EXEC подтверждает Павел.
- Исходники: `5de28b55`, ветка `perf/tests-ci-runtime`, Windows/PowerShell, .NET SDK 10.0.402, Debug, TUnit 1.44.0.
- Поверхность: Codex desktop; model eval неприменим — меняется запуск существующих .NET тестов.
- Профиль: performance-optimization, testing-dotnet; основной stack dotnet-desktop-client.
- Instruction stack: central AGENTS/routing → creator-vibe-lens/model-behavior/tool-execution → quest-governance/quest-mode/collaboration/testing → указанные context/profile → spec-linter/spec-rubric/review-loops → локальный AGENTS.override.
- Canonical template: `C:\Users\Kibnet\.codex\agents\templates\specs\_template.md`.
- Связь: предыдущая SPEC `2026-10-19-test-runtime-optimization.md`; её реализация ускорила отчётность, но исходная потребность в ускорении исполнения остаётся.
- Только локальная реализация/проверка/коммиты. Push, PR, merge, публикация и изменение GitHub settings не разрешены.

## 1. Overview / Цель
Исходное поручение: «А сами тесты как ускорить? Давай сократим время их выполнения без потери покрытия, а не только формирование отчётов».

Сократить wall-clock исполнения полного обязательного набора через два изолированных процесса основного проекта. Каждый прежний тест и каждый параметр должны выполниться с прежними assertions. Измерять интервал от подготовки рабочих копий/полного discovery до завершения всех обязательных test-процессов; время reporter в выигрыш не включать.

Output: локальный исполнимый запуск, CI wiring, contracts полноты и failure handling, сопоставимые замеры и TRX. Цель эксперимента — около 30% ускорения, минимальный критерий принятия — сокращение общего test wall-clock хотя бы на 15%. Фактический локальный результат — 40,94% (§11); это измерение одного хоста, не гарантия для каждого CI runner или узкого фильтра. При отсутствии эффекта кандидат следовало откатить и перейти к следующей гипотезе.

## 2. Текущее состояние (AS-IS)
- `Invoke-TestStage.ps1` запускает main и headless последовательно, `--maximum-parallel-tests 1` внутри каждого процесса.
- Внутри main общие Avalonia/ReactiveUI globals дополнительно ограничены ресурсами и limiter. Повышать общий TUnit parallelism без аудита нельзя.
- Сохранённый полный локальный baseline: main 1510/1510, 2750,876 с; headless первоначально 65/66, 254,099 с. Независимый полный повтор headless — 66/66, 263,857 с. Первое падение Git-индикатора остаётся исходной нестабильностью.
- Parent TRX main содержит 155 классов и 1510 cases. Сумма parent durations 3342,579 с больше wall-clock из-за уже существующего перекрытия; её нельзя выдавать за последовательное время или складывать с child TRX.
- Крупные классы: MainControlTaskCardLayoutUiTests 279,34 с / 34 cases; MainControlTreeCommandsUiTests 258,82 / 45; RoadmapGraphUiTests 253,72 / 46; TaskTabEmojiFilterCopyUiTests 193,78 / 16. Это суммы parent durations, не независимые стадии.
- NightAgentWorkflowReplayTests: два cases, 311,585 с, из них реальный benchmark 3000×600 — 309,477 с. Его нагрузка/изоляция сохраняются.
- Простая LPT-модель по классам без NightAgentWorkflowReplayTests даёт двум группам по 1515,5 с. Она игнорирует contention, startup и текущее перекрытие и служит только основанием для эксперимента.
- Свежий EventPipe профиль: один layout case прошёл за 5,150 с. В повторе группы Desktop* один case упал на прежнем compact-row assertion, другой прошёл; production/test source не менялись. Это диагностический failure, не исправление и не зелёный baseline. Inclusive async stacks включают continuations; нельзя приписывать их полное время миграции fixtures.
- В установленном TUnit есть `--filter-uid`; текущие rendered children уже выбирают ровно один stable UID и требуют один passed TRX. В TUnit.Core 1.44.0 проверен `TestDiscoveryContext.AllTests` для получения полного manifest из after-discovery hook. Console `--list-tests` выводит имена без UID и не подходит как машинный inventory.
- Fixtures в основном используют GUID paths, но AppContext.BaseDirectory/CurrentDirectory и relative artifact paths требуют изоляции. Аудит не считать завершённым до EXEC.
- Evidence: `%TEMP%\unlimotion-test-exec-20261020-14fd\serial`, `baseline-confirmation\headless`; свежие traces/TRX — `%TEMP%\unlimotion-test-body-profile-20261020`.

## 3. Проблема
Основная длительная UI-нагрузка находится в одном процессе. Перекрытие только main/headless ограничивалось примерно 8,46%; разделение самого main затрагивает значительно большую долю времени.

## 4. Цели дизайна
Два независимых процесса, прежняя последовательность внутри процесса, обязательный полный inventory, явные failures, воспроизводимая диагностика и сохранённый последовательный путь запуска.

## 5. Non-Goals
- Не удалять/skip-ить cases, параметры, assertions, графовые/визуальные матрицы; не уменьшать benchmark.
- Не сокращать таймауты, debounce и отрицательные observation windows ради скорости.
- Не менять production код, storage semantics, snapshots, визуальные эталоны или сценарии пользователя.
- Не менять число GitHub jobs/runner, имя required check All tests или native FlaUI workflow.
- Не выдавать inventory equality за доказательство равного line/branch coverage: сохранение качества здесь обеспечивают неизменные тела тестов, assertions и полное исполнение cases. Если существующий coverage gate включён — он сохраняется.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- Новый main discovery hook: по явному orchestration request атомарно сохранить schema version, stable UID, class, method, display name и полный набор cases; без запроса ничего не менять.
- Новый coordinator в `scripts/ci/`: discovery, планирование двух worker lanes, рабочие копии, запуск, полнота, exit и cleanup.
- `Invoke-TestStage.ps1`/metadata и reporter: корректно представить несколько invocations одного проекта, сохраняя nested child results и исходные failures.
- Workflow: вызвать полный ускоренный путь после прежних последовательных builds; сохранить always-report/upload.

### 6.2 Детальный дизайн
1. Получить полный inventory main штатным discovery той же сборки. Проверить hook на list-only запуске; если hook не вызывается или возвращает неполный набор, завершить orchestration failure. Не переходить к парсингу красивого console output или к неполному hardcoded списку.
2. Отделить NightAgentWorkflowReplayTests целиком в exclusive lane; выполнить её после остальных test-процессов. Все cases класса сохраняются, включая 3000×600 и process-wide allocation measurement. Headless выполнять отдельной последовательной стадией до benchmark; одновременно с benchmark другой test-процесс coordinator не работает.
3. Остальные main cases распределить детерминированно между двумя lanes по классам. Использовать небольшой проверенный class-cost manifest для тяжёлых классов; неизвестный класс автоматически включать с положительным default weight, затем LPT с tie-break по полному имени. Вес влияет только на скорость, не на обязательность исполнения. Исключение из совместного class batch: методы/классы с test-only `FreshTestProcess` запускаются по одному UID в свежем процессе. В текущем наборе это 23 случая настоящего Skia rendering; их нельзя смешивать с semantic Headless typefaces даже внутри одного класса. Внутренние циклы/matrices/cold+warm flows каждого случая не делятся.
4. Выполнять выбранные stable UID штатным `--filter-uid`, сохраняя maximum-parallel-tests=1. До старта проверить длину Windows command line. При необходимости разбить lane на последовательные batches, сохраняя класс целиком; один слишком большой класс запускать штатным точным class tree filter отдельным batch с обязательной UID-сверкой. Не обрезать аргументы, не выбирать первые N cases. Правила передачи нескольких UID подтвердить contracts на установленном runner до полного прогона.
5. Каждому worker предоставить собственную копию test outputs/content, CurrentDirectory, trace/results/artifacts directories. Копии размещать в уникальном каталоге под checkout, чтобы repository-relative source contracts продолжали находить настоящий repo. Builds и общие исходники не копировать/менять конкурентно. Проверить explicit relative paths, Git/global config, ports, subprocesses, snapshot reads, writable sources и cleanup; реальную внешнюю общую mutable зависимость выполнять последовательно либо изолировать test-only окружением. Не переносить меняющие общие files тесты в concurrent lanes до закрытия находки.
6. Сохранять per-invocation metadata и PID ownership. Дождаться всех запущенных invocations даже при failure одного. Cancellation/launch failure/timeout остаются failure; teardown завершает только собственные process trees. Выходные logs сохраняются и при исключении.
7. Каждый started/finished trace дополнить stable UID. После запуска сравнить мультимножество parent execution UID с discovery manifest: каждый UID ровно один раз, нет неожиданных UID, пропусков, skipped вместо прежнего pass или только начатых cases. Nested rendered child UID/result учитывать отдельно: не объявлять его дубликатом родителя. Передать reporter все invocations, не потерять failed batch при последующем успешном batch.
8. Сохранить обычную команду `dotnet test` как serial fallback. Для тех же 23 случаев существующий rendered-child bootstrap обобщён в `RenderedTestProcess`: обычный runner делегирует тот же stable UID в child и требует ровно один passed TRX; coordinator уже даёт свежий процесс и ставит child flag, исключая лишний вложенный запуск. Старые TabEmoji таймауты 3 минуты сохранены; для новых rendered wrappers дополнительный deadline не вводится, используется TUnit cancellation. Ускоренный локальный script — тот же entry point, который вызывает workflow. Не делать более слабый fast subset командой полного запуска.
9. Test-only Headless wrapper следит также за worker Task Avalonia: при fault worker до завершения Dispatch передать исходное исключение, не висеть на потерянном promise. Доступ к private `_dispatchTask` привязан к текущему Avalonia 12.0.4; отдельный regression test инжектирует worker failure, дополнительные проверки сохраняют generic result/UI affinity и exception/cancellation semantics. Никакое исключение worker не превращается в PASS.

Visual planning: production UI, пользовательские действия, геометрические условия assertions и эталоны не меняются. Проверяется инфраструктура исполнения UI suites. В EXEC допустимо перемерить фактические bounds перед существующим геометрическим snapshot/assertion. Evidence — diff test helper, повтор существующего cold UI case, TRX, UID inventory и lifecycle timeline; существующие PNG сохраняются как artifacts. Это конкретизация обязательной стабильности исполнения, не новая UI-фича. Video fallback: изменяемый raw Avalonia.Headless harness сохраняет PNG, но не имеет интегрированного video recorder; next-best evidence — rendered PNG из тех же автоматизированных cases и TRX. Проверочная команда: `dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter '/*/*/MainControlFilterToolbarResponsiveUiTests/*' --report-trx`.

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Expected output | Evidence | AC |
| --- | --- | --- | --- | --- |
| Полный запуск | Локальная команда либо будущий workflow | Все прежние cases быстрее, оба проекта обязательны | Paired wall-clock + inventory/TRX | AC1–3 |
| Один worker падает | Test/start/telemetry failure | Общий failure, остальные результаты сохранены | Negative contracts | AC4 |
| Новый тест | Добавлен неизвестный класс/параметр | Автоматически попал ровно в один lane | Planner/discovery contracts | AC1 |
| Нестабильный UI case | Layout/Git-indicator assertion падает | Failure виден с trace, нет автоматической подмены pass повтором | TRX + reporter contracts | AC4 |

### 6.4 State / Interaction Matrix
| State | Trigger | Result | Edge case |
| --- | --- | --- | --- |
| Discovery готов | Планирование | Полное disjoint partition | Duplicate/empty UID → failure |
| Два worker работают | Один failure | Дождаться обоих | Launch failure тоже сохраняется |
| Workers завершены | Headless, затем benchmark | Обязательные стадии выполняются | Не fail-fast на прежней ошибке |
| Все стадии завершены | Validation | UID/outcome reconciliation | Пропуск/дубликат/нет finished → failure |
| Cancellation | Cleanup | Только собственные процессы | Результат не PASS |

### 6.5 Decision Ledger
| Decision | Owner | Chosen option | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Число workers | agent | Два main lanes в одном job | 0.85 | CPU/IO contention | Нет |
| Изоляция UI | agent | Разные процессы и writable output roots | 0.95 | Непроверенный внешний общий ресурс | Нет; обязательный audit в EXEC |
| Benchmark | agent | Весь класс после всех других suite processes | 0.95 | Дополнительный последовательный хвост | Нет |
| Сохранение качества | agent | Тела/args/assertions неизменны; UID reconciliation | 0.95 | Ошибка discovery/partition | Нет |
| Материально новый scope | user | Новая SPEC вместо прежней report-only реализации | 1.0 | Предыдущее approval не покрывало split main | Получено «Спеку подтверждаю» 2026-10-20 |

### 6.6 Runtime / Config / Data Contract Matrix
| Area | Source of truth | Change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Inventory | TUnit discovery той же сборки | Machine manifest | Старые обычные test commands сохраняются | Full TRX/UID comparison |
| All tests | tests.yml | Test entry point | Прежнее имя, fail-closed gate | Workflow contracts |
| Results | Invocation metadata/TRX/trace | Несколько main invocations | Parent/child различимы, failures не теряются | Reporter fixtures + negative batches |
| Production data | App/storage sources | Нет изменений | Нет migration/rollout | Diff review |

## 7. Алгоритмы / инварианты
Partition union равен discovery; pairwise intersection пусто. Stable identity не заменять displayName или порядковым номером. Class weights не являются allowlist. Benchmark не конкурентен suite processes. Все пути удаления проверяются на принадлежность уникальному run root; сохранять нужные failure artifacts до cleanup.

## 8. Интеграция
Builds → discovery/worker preparation → два main lanes → headless → exclusive NightAgentWorkflowReplayTests → reconciliation → always reporter/upload. Failure ранее начатой стадии не отменяет остальные обязательные проверки; build failure не запускает несобранный проект и не даёт общего PASS.

## 9. Модель данных
Только schema-versioned временные manifests/plans и per-invocation metadata. Приложение и persisted task data не меняются. Новый optional stable UID в trace; reporter поддерживает прежние artifacts без него.

## 10. Rollout / Rollback
Сначала локальная реализация и доказательства. CI wiring остаётся локальным diff до отдельного разрешения публикации. При регрессии вернуть прежний test entry point и убрать coordinator/hook; оптимизация reporter из 5de28b55 независима и сохраняется. Удалять только созданные данным run рабочие копии после сохранения evidence.

## 11. Проверки и Acceptance Criteria
- AC1: каждый прежний main/headless case и параметр исполняется с прежними assertions; нет потери/дублирования/skip. Inventory изменения ограничены новыми infrastructure contracts.
- AC2: три paired targeted runs одних тяжёлых классов демонстрируют стабильное направление выигрыша; baseline и candidate не запускаются одновременно. Затем свежие полные serial/candidate runs на одной сборке/хосте/Debug. В wall-clock включены discovery, copies, все batches, headless, benchmark, reconciliation; исключены builds и reporter одинаково с обеих сторон.
- AC3: общий test wall-clock сокращён минимум на 15%; целевой ориентир около 30%. Измерение memory/allocations — secondary diagnostic: сохранить benchmark evidence и peak memory worker processes, проверить отсутствие OOM. При шуме повторить только необходимую пару.
- AC4: contracts покрывают неизвестный класс/параметр, duplicate/missing UID, command-line overflow, ambiguous identity, zero discovery, child distinction, test/start failure, cancellation ownership, сохранение failed batch и продолжение других обязательных стадий.
- AC5: оба полных проекта зелёные, нет неразобранного candidate-only failure; baseline flakes отдельно записаны с first-attempt evidence. Автоматические retries не делают failed invocation успешным. Если исходная нестабильность мешает сопоставлению, исследовать её и явно разделить исправление от performance evidence.
- AC6: обычный serial fallback и новый локальный полный entry point работают; workflow contracts, reporter contracts и diff checks проходят.

Команды: прежние `Invoke-TestStage.ps1 -Stage test -Project main/headless -ResultsRoot <fresh>` для baseline; новый `Invoke-TestExecution.ps1 -ResultsRoot <fresh>` для candidate. Targeted — TUnit `--treenode-filter '/*/*/MainControlTaskCardLayoutUiTests/*'`, затем другие тяжёлые UI классы; минимум expected tests сверять с discovery, использовать уникальные results roots. Точный helper signature фиксируется в реализации; смена внутреннего имени не меняет scope.

### Acceptance-to-Test Matrix
| AC | Verification | Required evidence | Status |
| --- | --- | --- | --- |
| 1 | Discovery/partition/full reconciliation + test body diff | UID multisets, parameterized counts, parent/child TRX | PASS: 1510 прежних UID сохранены, добавлены 3 regression cases; 1513/66 exact UID equality, без skips |
| 2–3 | Targeted pairs, full serial/candidate | Timing JSON + process timeline + memory | PASS: полный wall-clock −40,94%; 4 короткие пары, медиана −17,08%, один шумный противоположный результат сохранён |
| 4 | Planner/process/reporter negative contracts | Failing stubs и explicit assertions | PASS: 11 параметризованных сценариев, native launch failure, owned cancellation, hidden child failure |
| 5 | Full main/headless и разбор failures | Первичные логи/TRX без silent retry | PASS: оба полных запуска 1513/1513 + 66/66; v2 с первой попытки; исходные flakes и первый hang сохранены и разобраны |
| 6 | Fallback/candidate/workflow/reporter checks | Exit/logs/diff | PASS: прежний serial entry point, новый полный entry point, reporter/workflow contracts, PowerShell parser и diff check |

### Результаты EXEC
Корень evidence: `%TEMP%/unlimotion-sharding-validation-20261020-14fd-v2`. Windows, SDK 10.0.402, TUnit 1.44.0, Debug. Builds/profilers не запускались параллельно с замерами. `assemblies.json` и финальная проверка подтверждают неизменность обоих test DLL: main `E97EEFCFAF4F…`, headless `4C9B861244D2…`. Source case actions, Arguments, геометрические thresholds и benchmark constants сохранены; изменены test harness/bootstrap, добавлены 3 regression cases.

| Метрика | Serial fallback | Два workers |
| --- | ---: | ---: |
| Полный outer wall-clock, без builds/reporter | 2 645 838,9919 мс (44:05,839) | 1 562 505,9229 мс (26:02,506) |
| Canonical main cases Passed/Failed/Skipped | 1513 / 0 / 0 | 1513 / 0 / 0 |
| Canonical headless cases Passed/Failed/Skipped | 66 / 0 / 0 | 66 / 0 / 0 |
| Все TRX records вместе с nested children | 1635 | 1612 |
| Reporter telemetry errors | 0 | 0 |
| Benchmark legacy successful parses / complete reads | 1 800 000 / 600 | 1 800 000 / 600 |
| Benchmark legacy elapsed | 307 828,1800 мс | 502 450,5735 мс |
| Benchmark legacy process allocations delta | 22 406 663 088 bytes | 22 409 779 216 bytes |

Полное ускорение **40,9448%**, экономия **18:03,333**. В candidate включены discovery, три deployment copies, все invocations, Headless, benchmark, reconciliation, сохранение артефактов и cleanup. Внутренний stopwatch coordinator — 1 562 098,4296 мс; для сравнения использован внешний интервал обоих entry points, включая startup/exit оболочек. Изолированный benchmark медленнее; причина отдельно не профилировалась, это не объявляется улучшением самого benchmark. Нагрузка не уменьшалась: fixture/graphCoverage/noOp и счётчики observation/legacy/offline совпали, включая 3000 nodes, 600 targets, 60 multi-parent targets, 1260 canonical edges, 7260 context IDs и 366600 context edges. Allocations отличаются примерно на 0,014%.

Разница 23 в суммарных TRX records — это rendering bootstrap children. В serial их реальные действия выполнялись в nested children; в candidate эти же 23 UID выполняются непосредственно в fresh invocations. Полное canonical множество 1579 cases совпадает; каждый UID имеет started/finished и соответствующий parent TRX. Проверка не опирается на уменьшившееся число вложенных записей.

Targeted — `MainControlResetFiltersUiTests` + `MainControlFilterToolbarResponsiveUiTests`, 32 cases в каждом запуске:

| Пара | Workers=1, с | Workers=2, с | Порядок |
| --- | ---: | ---: | --- |
| 1 | 210,576 | 101,366 | 1 → 2 |
| 2 | 120,823 | 146,651 | 2 → 1 |
| 3 | 113,580 | 93,008 | 1 → 2 |
| 4, заранее выбранный повтор шумного протокола | 107,282 | 79,637 | 2 → 1 |

Все восемь прогонов PASS, 32/32 и UID/TRX reconciliation. Медианы 117,201 → 97,187 с (−17,08%); направление выигрыша подтверждено в 3/4 пар и полном наборе. Один обратный результат не отброшен: короткие фильтры шумные, ускорение каждого отдельного фильтра не обещается. Автоматических retries нет.

Candidate: 33 main invocations + headless + exclusive (35 test invocations), две дополнительные discovery invocations. Benchmark не пересекался ни с одним другим suite process по timestamps. Максимальный lifetime PeakWorkingSet одного test process — 1 880 502 272 bytes; это не измерение суммарной памяти всех workers. OOM не было. Baseline дополнительный snapshot PeakWorkingSet — 6 361 767 936 bytes, не используется как точный общий memory comparison.

Машинные доказательства: `verification.json`, `benchmark-verification.json`, `targeted-verification.json`, `full-serial-timing.json`, `full-candidate-timing.json`, оба `report-*/run.json`, inventories/plans/receipts/TRX/stages в `full-*`. Полные benchmark JSON скопированы в `benchmark-serial.json` и `benchmark-candidate.json`.

Финальные contracts: `unlimotion-execution-contract-7b96c5df2f87433fa55e07c1bbc9c654`, `unlimotion-report-contract-0740a7bd7c38424bac388c9a71ff4c38`, `unlimotion-orchestration-74cc81181545460c80b7a731904a6fdb` в `%TEMP%`. После полных замеров review добавил failure-only guards: Failed TRX при native exit 0 не даёт успеха; ошибка мониторинга Headless завершает owned child перед benchmark. Их normal/error paths проверены расширенными process contracts; C# test binaries не менялись.

Дополнительная визуальная проверка: `rendered-status-proof`, ровно 1 passed UID `TaskCardStatusRecovery_ErrorAndReloadRemainVisible(ru, Dark, 1400)` с `UNLIMOTION_STATUS_RECOVERY_RENDERED_EVIDENCE=1` и fresh-process flag. Non-flat pixel assertion прошёл; просмотрены свежие `artifacts/status-recovery/error-ru-Dark-1400.png` и `reload-menu-ru-Dark-1400.png`: видны реальный UI, ошибка и пункт «Обновить». Поиск сначала предположил другой cwd; служебное сравнение mtime затем исправлено с смешения UTC/local на DateTimeOffset. Это не падения теста. Video fallback — PNG/TRX: raw Avalonia.Headless harness не подключён к video recorder; native FlaUI/desktop acceptance и GitHub CI не запускались.

### Expected User Review Objections
| Objection | Answer / проверка |
| --- | --- |
| «Опять ускорены только отчёты» | AC2–3 полностью исключают reporter и включают worker preparation. |
| «Часть тестов просто не запустилась» | Manifest и завершившиеся UID равны; новый класс автоматически включён. |
| «Параллелизм сломал UI» | Процессы/outputs раздельны, limiters сохранены; full evidence обязательно. |
| «Benchmark стал меньше или измеряется на загруженном host» | 3000×600 неизменно, coordinator не запускает другие suite процессы одновременно. |
| «Результат назван готовым по расчёту» | LPT — только гипотеза; без замеров и AC PASS завершения нет. |

## 12. Риски
CPU/IO contention и startup overhead могут съесть выигрыш; сохранять только измеренно полезную схему. Общее внешнее состояние, source-path contracts, длинные command lines и потеря failed invocation защищаются явными проверками. Нестабильные UI assertions уже воспроизведены до изменения; не ослаблять их и не приписывать заранее shard runner.
Смешанные Skia/Headless caches реально подтвердились dump: InvalidCastException из `HeadlessPlatformTypeface` в `SkiaTypeface` во время `Dispatcher.ResetForUnitTests` fault-ит worker, оставляя Dispatch pending. Митигируется fresh processes, а не игнорированием ошибки. Reflection worker guard при обновлении Avalonia требует повторной проверки regression test. Сравнение child inventory учитывает bootstrap: в обычном runner 23 rendered child records, в coordinator их реальные actions/assertions выполняются непосредственно в 23 fresh parent invocations; canonical parent UID set должен совпасть, это не потеря 23 тестов.

## 13. План выполнения
После approval: discovery/protocol characterization → audit изоляции и contracts → coordinator/metadata/reporter wiring → targeted pairs → fresh serial/candidate full runs → review/локальный commit. Публикация не входит в план. Реализационные решения внутри этих границ не требуют нового согласования всей SPEC.

## 14. Открытые вопросы
Блокирующих решений пользователя нет. Конкретные runner hook/UID transport и cross-process paths проверяются в EXEC до full runs; неподдержанный путь не заменяется слабым inventory.

## 15. Соответствие профилю
Hypothesis/measurement/risks/tests определены; сохранён полный before/after, каждый независимый optimization item коммитится отдельно. UI тесты исполняются целиком, production UI не меняется. Native desktop acceptance не заявляется.

## 16. Изменяемые файлы
| File | Responsibility |
| --- | --- |
| tests/Infrastructure/TestInventoryHooks.cs, compile links обоих проектов + lifecycle trace main | Stable inventory/UID; process-qualified UID в StdOut TRX для Unicode-safe reconciliation |
| MainControlTaskCardLayoutUiTests.cs, geometry helper/diagnostics | Перед snapshot закончить invalidated layout pass; actions/геометрические условия/данные не меняются |
| HeadlessSessionExtensions.cs, SafeHeadlessUnitTestSessionTests.cs | Worker fault propagation; 3 новые regression cases без изменения прежних |
| RenderedTestProcess.cs (обобщение TaskTabEmojiFilterRenderedProcess), UI methods с реальным rendering | Fresh-process annotation и bootstrap guards, прежние действия/параметры/матрицы сохранены |
| scripts/ci/Invoke-TestExecution.ps1 и его contracts | Полный изолированный запуск |
| scripts/ci/Invoke-TestStage.ps1, TestInvocationMetadata.ps1 при необходимости | Несколько invocations без перезаписи evidence |
| scripts/ci/Write-TestReport.ps1, Test-TestReport.ps1 | Агрегация invocations с прежней failure семантикой |
| .github/workflows/tests.yml и orchestration/workflow contracts | Обычный полный CI entry point |
| Эта SPEC | Замеры, audit, решение |

## 17. Было → стало
Один длительный main process → два изолированных main lanes и exclusive benchmark. Один invocation main → reconciled несколько invocations. Прежние actions/parameters/assertion conditions → без изменений; bootstrap/harness исправлены, добавлены 3 regression cases (main 1510 → 1513).

## 18. Альтернативы
- Только main/headless overlap: прежний предел 8,46%, недостаточен для этого этапа.
- Глобально повысить TUnit parallelism: общие Avalonia globals, высокий риск.
- Уменьшить benchmark/матрицы: нарушает поручение.
- Кешировать мигрированные fixtures/заменить disk storage памятью: меняет условия сценариев; профиль не подтвердил достаточный изолированный эффект. Не включать без новой доказанной гипотезы.
- Два GitHub jobs: дополнительные runners/check delivery; здесь не требуется.

## 19. Quality gate / review
### SPEC Linter
1 PASS — исходный outcome в §1; 2 PASS — свежие и сохранённые evidence §2; 3 PASS — bottleneck §3; 4 PASS — invariants §4; 5 PASS — границы §5.
6 PASS — ownership §6.1; 7 PASS — integration §8; 8 PASS — algorithm §6–7; 9 PASS — failures §6.2; 10 PASS — измерение/порог §11.
11 PASS — временные schemas §9; 12 PASS — fallback/старые artifacts §9–10; 13 PASS — rollback §10; 14 PASS — AC1–6; 15 PASS — matrix/negative cases; 16 PASS — команды/stop §11.
17 PASS — dependencies §13; 18 PASS — ledger/questions §6.5/14; 19 PASS — expanded/medium §0; 20 PASS — profile §15. Итог: ГОТОВО для approval, не свидетельство готовой реализации.

### SPEC Rubric
Цель/границы 5; AS-IS 5; дизайн 5; безопасность/rollback 5; проверяемость 5; автономность 5. 30/30: решение и проверяемые ограничения определены; эффект ещё не доказан.

### Role-Based Review Result
| Role | Verdict | Evidence |
| --- | --- | --- |
| Domain workflow | PASS | Исходное ускорение test execution сохранено, отчётность исключена из metric. |
| UX / designer | Не применимо | Пользовательский UI и эталоны неизменны; исполнение старых UI cases обязательно. |
| Tester | PASS | UID equality, parameters, negative invocations, flakes и full tests §11. |
| Developer / architect | PASS | Процессная изоляция, bounded UID transport и serial fallback. |
| Delivery / operations | PASS | Прежний check, ownership cleanup, no publication, failure aggregation. |

### Post-SPEC Review
- Scope reviewed: эта SPEC, прежняя SPEC/non-goals, Invoke-TestStage, TestInvocationMetadata, Write-TestReport, lifecycle hooks, HeadlessSessionExtensions, rendered child runner, fixture paths, TUnit.Core discovery API и fresh profile/TRX.
- Review passes: Scope/Evidence — отделены реальные timings от LPT; Contract — полный inventory и benchmark; Adversarial — неизвестные тесты, overflow, shared outputs, silent retries, child duplicate; Role-based — таблица выше.
- Findings/fixes: MEDIUM — сумма parent durations могла стать ложным wall-clock forecast; исправлено §2/11. MEDIUM — async profile inclusive attribution не доказывает migration bottleneck; исключена неподтверждённая fixture-cache оптимизация. MEDIUM — несколько main batches могут перезаписать failure metadata; введены immutable per-invocation results и negative contract. MEDIUM — fixed inventory мог терять новые cases; заменён discovery manifest и unknown-class planning.
- Fix and re-review: проверены исправленные invariants и AC1–6, исходная просьба и no-reporter metric. Новых blocking findings текущей SPEC нет.
- Depth checklist: scope/границы, исходный сценарий, evidence, unsupported claims, regression/edge cases, comments/docs, hidden delivery change, manual-review challenge покрыты §§1–18. Независимый review для medium не обязателен; выполнен self-review, не называется independent.
- Stop decision: PASS для запроса exact approval; implementation/full candidate evidence отсутствуют и не заявляются. Остаточная engineering uncertainty — скорость при contention, проверяется experiment gate.

### Post-EXEC Review
- Scope/evidence: просмотрен полный tracked/untracked diff, coordinator/planner/hooks/worker guard/rendered bootstrap, reporter/workflow и process fixtures. Production код и baseline images не менялись.
- Findings/fixes: MEDIUM — worker fault мог оставлять Dispatch pending, добавлен fault propagation с RED/GREEN regression. MEDIUM — реальные и semantic renderers смешивали font caches, 23 cases изолированы без изменения действий. MEDIUM — cold geometry snapshot видел stale measured widths, завершён реальный layout panel перед прежними assertions (12/12 cold passes). MEDIUM — native exit 0 мог противоречить Failed TRX, добавлен fail-closed contract. MEDIUM — monitoring exception Headless не должна допускать benchmark рядом с owned child, добавлен cleanup guard.
- Adversarial review: unknown/new classes, UID duplicates/omissions, command-line overflow, Unicode/control names, nested parent identity, zero discovery, skipped/failed TRX, native launch failure, cancellation ownership, metadata overwrite и скрытый child failure проверены contracts. Пороги/таймауты прежних тестов не уменьшались.
- Role review: Domain PASS — полный test execution −40,94%; Tester PASS — canonical UID/parameters/assertions сохранены, два full green + regression/negative/UI evidence; Developer PASS — изоляция, bounded transport и fallback; Delivery PASS локально — прежний check и always reporting, публикации нет; UX — production UI без изменений, реальный rendered кадр проверен. Это self-review, не independent review.
- Re-review: финальные process/reporter/workflow contracts PASS, PS parser PASS, diff check PASS; после последних failure-only guards не повторялся весь 26-минутный success workload, их ветки проверены contracts. Пересборка C# не требовалась.
- Остаток: частный Avalonia field привязан к 12.0.4 и защищён regression; короткие timings шумные; isolated benchmark медленнее; внешний CI и native desktop не проверены. Эти границы не заменяются claims о live delivery.
- Stop decision: PASS — локальная реализация и AC1–6 завершены, порог 15% и ориентир 30% превышены, неразобранных candidate-only failures нет. Локальные коммиты разделяют harness corrections и execution optimization. Push/PR/merge не выполнялись.

## Approval
Получено «Спеку подтверждаю» 2026-10-20. Фаза EXEC, повторное подтверждение в неизменном scope не требуется.

### Delivery review, 2026-10-21
Пользователь поручил «Влей в мейн»: push, PR и merge этой реализации разрешены. Перед публикацией интегрирован текущий main `1ed7e22b` без конфликтов; сохранён актуальный CI timeout 60 минут. Создан PR #327.

Автоматический review выявил P1: coordinator переопределял внешний `UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY`, помещая дочерние importance TRX внутрь ResultsRoot. Это делало parent identity неоднозначным для history и увеличивало report counts повторно; canonical UID reconciliation оставался корректным. Исторические report totals 1635/1612 относятся к замеру до этого исправления и не являются целевой статистикой исправленного reporter. Полные timings и равенство canonical 1513+66 cases сохраняют силу.

Исправление сохраняет настроенный внешний root; локальный default — соседний `<ResultsRoot>.importance`, с указателем в execution.json. Явный вложенный root отклоняется до записи результатов. Fixture воспроизводит дочерний rendered TRX: RED — повторный учёт/ambiguous identity; GREEN — 4 canonical main records с complete identity, внешний root сохранён, default evidence переживает cleanup, вложенный root отклонён. Все eleven failure/success scenarios, launch failure, cancellation и reconciliation прошли: `%TEMP%/unlimotion-execution-contract-78c3bdb26ed24c8fafbfcd0b0de32db4`. Полная проверка интегрированного кода выполняется GitHub CI на финальном head PR перед merge; прежний локальный full run не выдаётся за проверку обновлённого main.

CI run `39541333857` на `91116382` подтвердил main: 1534 canonical cases, inventoryComplete=true, exit=0; все importance identities complete. CodeQL, Android и installed Windows lifecycle прошли. Полный check упал из-за одного Headless-сценария: `GitIndicator_SaveSyncFailureSwitchKeyboardAndRenderedMatrix` ожидал Idle после fixture commit, но отложенное autosave заголовка могло завершиться после commit и законно вернуть Dirty. Та же ошибка воспроизведена локально на обновлённой сборке (`%TEMP%/unlimotion-pr327-git-ui/original`, RED 1/1). Перед synthetic retry commit теперь завершаются реальные pending write producers через существующий SealPendingSaves; обычное сохранение, языки/темы, клавиатура, переходы и timeout/assertions сохранены. Весь класс GREEN 5/5 (`fixed-class`), актуальный rendered PNG unknown-space-B просмотрен. Это test-fixture synchronization, production код не меняется. Повторный полный CI обязателен на новом head; неудачный run сохранён.

Дополнительный fresh-process повтор выявил зависание промежуточного drain без UI pump; положительный class run сам по себе не принят за окончательную устойчивость. Dump `%TEMP%/unlimotion-pr327-git-ui/repeat-hang.dmp` и `dumpasync` показали цепочку AtomicWriteAllTextAsync → DrainPendingEditorChangesAsync → SealPendingSavesCoreAsync, ожидающую Headless dispatcher. Остановлен только принадлежащий проверке child; run сохранён как failed diagnostic. Как и для первого ordinary save в этом же тесте, завершение pendingSaves теперь ожидается с Dispatcher.RunJobs и прежним UiAssert timeout, затем await проверяет failure записи. Финальные `pumped-repeat-1..3`: 3/3 свежих parent/child запусков прошли без retries и skips. CI промежуточного `5f6ca9df` отменён, новый head требует полного CI.

Финальная проверка `26e16164`: локальный полный Headless 66/66 с inventoryComplete=true и UID/TRX reconciliation (`%TEMP%/unlimotion-pr327-final-headless`); CI `39544133227` также Headless 66/66. Main выполнил все canonical cases, но один phone geometry-case (360) поймал stale WrapPanel layout: planning group left=-2 при viewport=310. Локальная исходная phone-матрица воспроизвела тот же дефект при 430 (left=-4); ещё два исходных повтора прошли, что сохранено как свидетельство гонки. Ранее завершение реального planning layout было только перед desktop-row snapshot; теперь тот же helper вызывается перед чтением phone planning edges. Width/margin/positions не назначаются вручную, production UI и прежний допуск containment 1 px не меняются. После исправления четыре свежих матрицы 360/390/430 GREEN: 12/12 (`%TEMP%/unlimotion-pr327-phone-layout/fixed-1..4`). Неудачный CI сохранён; требуется новый полный CI на финальном head.

## Журнал действий агента
| Фаза / событие | Решение | Evidence / остаток | Следующий шаг | Решение человека |
| --- | --- | --- | --- | --- |
| SPEC, 2026-10-20 | Исследовать execution и разделить main на два изолированных lanes | Parent TRX 1510 cases, fresh 1/1 профиль, повтор Desktop* 1/2; код неизменен. Два неудачных диагностических OR-фильтра дали 0 tests/ненулевой exit и не являются coverage evidence. | Exact approval, затем characterization/реализация/полные замеры | Ожидается |
| EXEC, 2026-10-20 | Approval получено; начать discovery/protocol characterization | Включён opt-in inventory/UID hook; обычные тестовые сценарии неизменны | Build и list-only/UID проверка | «Спеку подтверждаю» |
| EXEC, characterization | Discovery 1510 main / 66 headless; 2 UID runner probe прошёл. Process/reporter/workflow contracts прошли | Inventory + TRX + logs в `%TEMP%/unlimotion-shard-probe-20261020` и `unlimotion-execution-contract-*` | UI targeted, затем full pair | Не требовалось |
| EXEC, review corrections | Сверка displayName непригодна для escaped surrogate/control cases: добавлен process-qualified UID marker в TRX. В cold layout выявлен исходный compact-row failure: после изменения measured widths добавлен финальный arrange в helper | Первый serial 79/79 за 300,833 с, concurrent 78/79 за 140,875 с. Это diagnostic evidence, не принятый performance PASS. Исходный такой же failure до изменений сохранён в EventPipe probe | Повтор cold case; новые paired измерения на одной сборке | Конкретизация in-scope validation, actions/assertions/production UI прежние |
| EXEC, additional review | Startup arrange/render не устраняет поздние layout invalidations между awaited assertions; промежуточные setup edits удалены. Проверка действительного layout перенесена непосредственно перед синхронным geometry snapshot. Дополнительная диагностика widths/bounds/validity сохраняется | Промежуточные cold-final 9/10, cold-settled 2/3 — не PASS. Один преждевременный diagnostic run во время build отменён и исключён; build завершился успешно после освобождения DLL. Unicode/control suites 32/32 reconciled. Финальные infrastructure contracts: 10 scenarios + owned cancellation PASS (`unlimotion-execution-contract-0abc33a6c49240bc9db4426c21fbd0f4`) | Cold acceptance, затем quiet paired/full runs | Не требовалось |
| EXEC, cold acceptance | Перемерка фактической planning panel непосредственно перед geometry snapshot устранила stale cache; геометрические thresholds, пользовательские actions и production UI не менялись. Startup waits и промежуточные guards удалены | `cold-remeasure-1..12`: 12/12 passed + UID/TRX reconciliation. Обе сборки успешны. Точное полное множество discovery main совпало с baseline по 155 class counts / 1510 cases | Код фиксируется для quiet paired/full runs; driver/evidence в `%TEMP%/unlimotion-sharding-validation-20261020-14fd` | Не требовалось |
| EXEC, runtime blocker | Первый quiet pair остановлен и не принят: semantic/Skia смешение fault-ило Headless worker при ResetForUnitTests, оставляя Dispatch pending. MCP debug tool отсутствует; сохранены dump/async stacks и fault stack, остановлен только принадлежащий run PID 195944 | `%TEMP%/unlimotion-sharding-validation-20261020-14fd/targeted-hang*`; InvalidCastException HeadlessPlatformTypeface → SkiaTypeface. Это не self-join/Dispose гипотеза: dump её уточнил. Первая попытка FAILED сохранена | Разделение реальных rendered cases + worker-fault guard | Не требовалось: предусмотренная test-only изоляция общего runtime state |
| EXEC, runtime fixes verified | Worker failure regression RED на старом wrapper (TimeoutException вместо исходного failure), GREEN 5/5 после guard; generic result/UI affinity и exception/cancellation сохранены. Fresh metadata 23 cases; main 1513, headless 66. Обычный CLI toolbar subset 22/22 прошёл с rendered bootstrap | `worker-guard-red`, `worker-guard-green`, `toolbar-legacy-isolated`; contracts `unlimotion-execution-contract-c0b712f9b4234f6ca86d47870f3d6fa7` PASS. Старый TabEmoji timeout 3 min сохранён, новые cases не получили сокращённого deadline | Повтор quiet validation в `%TEMP%/unlimotion-sharding-validation-20261020-14fd-v2` | Не требовалось |
| EXEC, targeted pairs | Три пары 32 UI cases: serial/candidate 210,576/101,366 с; 120,823/146,651 с (обратный порядок); 113,580/93,008 с. Все шесть прогонов PASS, UID/TRX reconciled. Разброс включает одно замедление; это не доказательство стабильного выигрыша полного набора | `unlimotion-sharding-validation-20261020-14fd-v2/state.json`, отдельные immutable roots. Original 1510 UID целиком входят в final 1513; добавлены ровно три SafeHeadless regression cases | Полная пара без параллельных builds/profilers; при необходимости повтор noisy targeted pair, сохранив все результаты | Не требовалось |
| EXEC, 2026-10-21, full acceptance | Serial 44:05,839 → candidate 26:02,506, −40,94%; 1513 + 66 cases Passed в обоих. Benchmark 3000×600 и все graph/context counts сохранены, isolated legacy phase медленнее. Повтор noisy protocol 107,282 → 79,637 с PASS | `verification.json`, `benchmark-verification.json`, `targeted-verification.json`, full TRX/stages и два zero-error reports; DLL hashes unchanged. Первая неудачная validation сохранена отдельно, принятый v2 — без retries | Final review, contracts и локальные коммиты | Не требовалось |
| EXEC, final review | Failure-only guards + расширенные contracts PASS; reporter/workflow/parser/diff PASS. Fresh status recovery pixel case 1/1, два актуальных PNG просмотрены | Contracts/evidence paths §11. Auxiliary PNG lookup/time comparison исправлены; actual tests не падали. GitHub/native acceptance не заявляются | Завершение локальной задачи | Публикация не разрешена и не выполнялась |
