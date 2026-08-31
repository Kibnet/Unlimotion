# Наблюдаемость, ускорение и стабильность тестового CI

## 0. Метаданные

- Статус: локальный EXEC реализован и проверен; результат PARTIAL по full performance (INCONCLUSIVE), локальная correctness PASS. Пользователь подтвердил спецификацию фразой «Спеку подтверждаю» 31.08.2026, затем отдельно разрешил Git delivery запросом «Оформи mr». История локальной приёмки ниже относится к состоянию до публикации ветки; merge и release не входят в эту авторизацию.
- Тип / профиль: `dotnet-desktop-client` + `ui-automation-testing`; context `performance-optimization`.
- Владелец: maintainers Unlimotion; заказчик — пользователь текущей задачи.
- Масштаб: large, поэтапное изменение CI, тестовой инфраструктуры и узкого UI-контракта.
- Целевое семейство / behavior baseline: Не применимо — модели и prompts не меняются.
- Поверхность: GitHub Actions, локальный Windows/TUnit, Avalonia UI.
- Effective runtime: текущий `global.json` — SDK 10.0.400, Microsoft.Testing.Platform; TUnit 1.44.0, Avalonia 12.0.4, AppAutomation 1.6.0. Версии не обновлять ради этой задачи. При EXEC записывать фактически разрешённые версии.
- Eval baseline / evidence: аудит 31.08.2026; `artifacts/ci-audit-2026-08-31/report.md`, `runs.json`, `ci-summary.json`, `local-results.json`, `baseline-concurrency.json`. Это локальные ignored-артефакты, не доступные автоматически читателю клона. Достаточный исходный срез продублирован в §2; исходные CI-ссылки приведены ниже.
- База реализации: текущий checkout `f39b32458aba0f7fe403b3bea26c14f9215d0507`, detached HEAD. При EXEC создать рабочую ветку `test/ci-observability-performance-stability` от этого checkout; не подмешивать `feat/daily-feed`. При изменившемся HEAD сначала проверить применимость и переснять baseline, не затирать пользовательские изменения.
- Целевой релиз: не назначается. Commit/push/PR/merge и изменение GitHub settings не входят в текущее разрешение на подготовку spec.
- Ограничения: никакого сокращения уникальных проверок, пропуска падающих тестов, автоматического rerun до green или увеличения таймаутов вместо диагностики.
- Источники: [CI #227](https://github.com/Kibnet/Unlimotion/actions/runs/32869083388), [#225](https://github.com/Kibnet/Unlimotion/actions/runs/32790027192), [#224](https://github.com/Kibnet/Unlimotion/actions/runs/32788668037), [#221](https://github.com/Kibnet/Unlimotion/actions/runs/32729322636), [#219](https://github.com/Kibnet/Unlimotion/actions/runs/32670677596).
- Связанные спецификации: `2026-06-02-pr-all-tests-check.md`, `2026-07-17-test-fixture-lifecycle.md`, `2026-07-19-headless-appautomation-storage-lifecycle.md` в этом каталоге. Их lifecycle-инварианты сохраняются; новая spec заменяет только прежнее предположение, что один CLI-флаг доказывает полную сериализацию.

## 1. Overview / Цель

Сделать медленные и нестабильные тесты измеримыми на уровне отдельного случая, убрать подтверждённое повторное тяжёлое исполнение и устранить воспроизводимую причину сбоя массового emoji-фильтра. Сохранить проверку файлового восстановления, реальные UI-сценарии и исполняемую связь с BDD.

Outcome contract:

- Success means: оба CI-safe проекта проверяются независимо от результата друг друга; результаты и длительности доступны как артефакты; оптимизации имеют сопоставимые замеры и карту сохранённого покрытия; emoji-проблема имеет доказанный RED/GREEN либо явно остаётся незавершённой.
- Итоговый output после EXEC: изменения из §16, машинные результаты, краткий CI summary, coverage map, отчёт до/после, regression evidence. После текущей SPEC-фазы — только этот документ.
- Stop rules: не начинать EXEC до «Спеку подтверждаю»; не менять production-контракт шире §6.2.6; не объявлять весь пакет завершённым, если emoji-причина не воспроизведена, full-run не green или обязательная проверка CI не выполнена. Завершённые этапы можно предъявлять отдельно, с точным остатком работы.

## 2. Текущее состояние (AS-IS)

Выборка аудита: ровно последние на момент проверки 10 завершённых запусков `Unlimotion Tests`, №218–227, 23–25 августа 2026, разные ветки/SHA, все attempt 1. Шесть успешны, три имеют test failure, один — ошибку компиляции Headless. Это **не 40% flaky tests**. Во всех 10 API вернул ноль загруженных артефактов; исторические длительности отдельных успешных тестов неизвестны.

- Основной набор вырос с 832 до 1247 тестов; CI-время 9:33–12:20, медиана около 11:18. Сравнивать эти времена как эффект оптимизации нельзя.
- `.github/workflows/tests.yml`: один `all-tests` / `All tests`, `windows-latest`, 30 минут. Последовательно restore, основной `dotnet test`, Headless `dotnet test`. При падении основного набора Headless пропускается. Его compilation failure в #219 обнаружился после более 10 минут основного набора.
- `--output Detailed` не дал в CI индивидуальных длительностей успешных тестов; TRX не включён, HTML не загружен.
- Полный локальный основной набор на дереве последнего CI: 1247/1247, 22:42.024; отдельный Headless локально в аудите не запускался. Источник — архив `3b2c9f195c0963189cd093b1ce95f0b735eb6893`, tree `70b59c3d8af7700730adec39c6245c0a69c09751`, совпадающий с CI merge-tree. Это более новое дерево Daily Feed, **не текущая база f39b3245**.
- В HTML обнаружено до трёх одновременно выполнявшихся `test body`, несмотря на `--maximum-parallel-tests 1`. Причина сочетания TUnit limiter/key/runner пока не установлена; это не доказательство ошибки конкретной библиотеки.

| Тест / группа | Локально в полном наборе | Отдельный запуск | Вывод для дизайна |
| --- | ---: | ---: | --- |
| `ActivationProjection_EveryPersistedWriteFault_RestoresPreviousRuntimeAndProjection` | 41.608 с | 25.324 с | Дорогая матрица дисковых отказов, чувствительность к общей нагрузке |
| `ServerStorageCrudRealtimeScenario_ExecutesFeatureSteps` | 39.440 с | 26.020 с | Повторное выполнение двух live-контрактов также из самостоятельных тестов |
| `CatalogMutation_EveryPersistedWriteFault_ReopensAsCompleteBeforeState` | 39.055 с | 25.957 с | Повторная подготовка данных каждого fault-case |
| `EmojiFilterScenario_ExecutesFeatureSteps` | 21.565 с | не измерялся | BDD вызывает пять уже зарегистрированных UI-тестов |
| `Breadcrumbs_ShouldRenderEmojiRunsWithEmojiFont` | 21.535 с | не измерялся | Кандидат на облегчение fixture; влияние первого запуска не отделено |

Суммы времени классов: `MainWindowViewModelTests` 235.104 с, `MainControlTreeCommandsUiTests` 191.000 с, `RoadmapGraphUiTests` 190.535 с. Это сумма длительностей, не вклад в wall-clock при перекрытии; эти классы не становятся автоматически целиком разрешённым scope рефакторинга.

| Исторический failure | Статус по аудиту | Что входит в реализацию |
| --- | --- | --- |
| `Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter`, #221 | `ArgumentOutOfRangeException` в Avalonia container insertion через обновление коллекции; 10/10 отдельных повторов прошли | Причина остаётся открытой: детерминированное воспроизведение, узкий фикс, UI-регрессия |
| `DeleteAsync_RetainsLateWriteFromHandleOpenedBeforeDelete`, #224 | Чтение до закрытия внешнего writer; уже исправлено в `e1870eb1c7dc7b0c8a525e2d899e358d094afb5c`; 10/10 повторов на новом дереве | Сохранить fix при будущей интеграции Daily Feed, не переносить feature в эту ветку |
| `Feed_MarkerlessHeadingWithUniqueCatalogName_UsesOnlyStableAreaId`, #225 | Cleanup до окончания конвертации; исправлено ожиданием `HasCreatedTask && !IsBusy` в `cc45eb663d5c24972ff24796a6cbe92e615efe72`; 10/10 повторов | Аналогично: условная проверка на дереве, где Feed уже присутствует |

Скрытые зависимости: глобальные ReactiveUI/Avalonia scheduler и registries; `NotInParallel("AvaloniaHeadless")` и `SharedUiStateParallelLimit`; отдельный server key; реальные watchers и pending saves в `MainWindowViewModelFixture`; `SafeHeadlessUnitTestSession` в `HeadlessSessionExtensions.cs`; BDD вызывает методы тестовых классов вручную. Fixture cleanup уже имеет producer seal/drain/dispose/delete — его нельзя упрощать до удаления каталога.

## 3. Проблема

У тестового контура нет проверяемого контракта стоимости и изоляции исполнения: одинаковая тяжёлая проверка запускается несколько раз, скрытые shared-state зависимости затрудняют воспроизведение, а CI не сохраняет данные для различения регрессии, гонки и ошибки сборки.

## 4. Цели дизайна

- Отделить build, test execution и reporting; не смешивать инфраструктурный отказ с test failure.
- Один канонический исполнитель каждого одинакового тяжёлого контракта; сохранить исполняемый BDD и уникальные assertions.
- Владение временными данными на уровне теста; изоляция shared-state подтверждается trace.
- Оптимизировать только измеренные пути, небольшими откатываемыми этапами.
- Не менять данные пользователя, публичные API, дизайн интерфейса, IDs элементов и правила фильтрации.

## 5. Non-Goals (чего НЕ делаем)

- Не добавляем Daily Feed в текущую ветку и не повторяем уже доставленные исправления.
- Не переписываем весь test suite, все STORM-контракты или инфраструктуру хранения.
- Не заменяем disk/recovery интеграцию exclusively in-memory тестами и не выбираем подмножество точек отказа.
- Не переводим все тесты в полностью serial/parallel режим без измерений; не создаём новые CI runners/shards в первом варианте.
- Не обновляем SDK/TUnit/Avalonia/AppAutomation, не меняем release/packaging workflows, branch protection и permissions ради удобства отчёта.
- Не включаем FlaUI, media generator и performance tooling в обязательный PR gate; два существующих CI-safe проекта остаются его границей.
- Не добавляем внешний сервис аналитики, постоянное хранилище истории, scheduled monitor или автоматические комментарии в PR.
- Не выполняем Git delivery на основании одного подтверждения spec.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент | Ответственность |
| --- | --- |
| `.github/workflows/tests.yml` | Независимые restore/build/test состояния двух проектов, неизменный check `All tests`, upload и summary |
| `scripts/ci/Write-TestReport.ps1` (новый) | Нормализация TRX + run metadata, Markdown/JSON; offline-агрегация предоставленной истории |
| `scripts/ci/Test-TestReport.ps1` (новый) | Самодостаточные fixtures и contract checks репортера без GitHub/production доступа |
| `scripts/ci/Invoke-TargetedTestSeries.ps1` (новый) | Ограниченная серия отдельных процессов, уникальные result dirs, проверка discovery/exit/timeout |
| `src/Unlimotion.Test` и узкие helpers | BDD deduplication, минимальная UI-fixture, fault-case diagnostics, isolation regression |
| `MainWindowViewModel.cs`, emoji control при доказанной необходимости | Только корректный порядок/thread affinity уведомлений для массового переключения фильтра |

### 6.2 Детальный дизайн

#### 6.2.1 CI без скрытого пропуска Headless

Сохранить один job с id `all-tests` и name `All tests`: меньше изменений status-check контракта и стоимости runner. Последовательность внутри job:

1. Checkout `github.sha`, SDK из `global.json`, текущий NuGet cache, подготовка локального feed.
2. Отдельные restore для Main и Headless, каждый с собственным step id и явным распространением native exit code.
3. Отдельные build обоих проектов, **до первого длинного test run**, `-c Debug --no-restore -p:UseSharedCompilation=false`.
4. Main test `--no-build --no-restore`; затем Headless test с такими же условиями. Каждый запускается, если его build успешен и job не отменён. Ошибка другого restore/build/test не блокирует этот запуск.
5. Report и upload при `always()` для доступных результатов. При cancellation/runner loss загрузка best effort, без обещания сохранения несуществующих файлов.

Для независимых шагов использовать явные status expressions (`!cancelled()` + успех собственных prerequisites), а не неявный `success()`. Не применять `continue-on-error` к build/test. Ни report, ни upload не могут превратить предыдущую ошибку в green. Общий результат green только при успешных обоих build/test и обязательном формировании/загрузке отчёта. Setup failure блокирует оба проекта; test failure одного не блокирует другой. Таймаут 30 минут и concurrency/cancel-in-progress сохраняются.

Граница независимости: проекты делят job и time budget; job cancellation/timeout/runner loss останавливает оба. Независимость от обычного test/build failure не означает независимость от падения машины. Если после оптимизаций 30 минут недостаточно, собрать evidence и отдельно пересмотреть topology; молча увеличивать лимит нельзя.

#### 6.2.2 Артефакты и достоверный отчёт

В обоих test invocations включить `--report-trx` и отдельный `--results-directory`. HTML, который создаёт установленный TUnit, явно собирать из его фактического output path; не предполагать, что все версии складывают HTML в TRX directory. Проверить наличие нужного MTP TRX extension в текущих зависимостях; использовать уже доступный extension, без обновления runner.

Контракт пути: `artifacts/test-results/<run-id>-<attempt>/<project>/`, где project — `main` или `headless`. Для local run — уникальный ID с timestamp. Выходы:

- оригинальные TRX и HTML;
- `run.json`: schemaVersion=1, repository, workflow, runId, runAttempt, event, ref, headSha, checkoutSha/treeSha, UTC start/end, OS/runner image, SDK/runtime/TUnit версии, configuration, точные test args, build/test step outcomes и exit codes, полнота telemetry;
- `tests.json`: project, stable logical test identity, display name/arguments, outcome, durationMs, executionId, timestamp; nullable phase timing и reason отсутствия;
- `summary.md`: результаты обоих проектов, build/test/report failures раздельно, top-20 длительностей и список failed/skipped/not-executed. `not-executed` не становится `passed`;
- отдельный phase/fault-case trace для инструментированных путей, если он был создан.

Nested BDD subcases имеют собственные IDs/outcomes/durations в trace, но не выдаются за дополнительные TUnit executions в totals. Summary показывает failed/not-executed subcases под родительским scenario. Из каждого native build/test invocation сохранить exit code до запуска других команд и явно завершить step этим кодом; metadata не должна восстанавливать его догадкой из строк лога.

Upload через официальный `actions/upload-artifact`, отдельное уникальное имя на run/attempt/project, retention 14 дней. Загрузка готовых файлов не требует передачи Actions runtime token тестовому процессу. Сохранять `contents: read`; не добавлять PAT, secrets или write permissions. Загружать только allowlist отчётов/trace из тестового output, не весь workspace, RavenDB/data/config/env dump. Тестовые данные синтетические. Ошибка upload/report на завершённом неотменённом запуске — видимая ошибка качества telemetry.

Отсутствие TRX/HTML после успешного test step — ошибка контракта. После build failure, runner crash или отмены отсутствие test reports допустимо только как явно `incomplete/not-executed`, с metadata и исходным status; успешный run из этого не получается. XML читать без external entities, HTML не исполнять при разборе, Markdown escaping для имён/ошибок, без повторной интерпретации содержимого как PowerShell.

Первичный источник test identity — проект + полное имя/класс/метод + аргументы. TRX execution UUID — идентификатор выполнения, не ключ истории. Если TRX не содержит достаточно различающих полей, дополнить TUnit metadata; коллизии или неполные identities показывать как ambiguous и не объединять молча. Оригиналы всегда сохранять.

История: тот же script принимает локальный каталог ранее скачанных артефактов (`-HistoryRoot`), выбирает последние 10 логических runs по UTC, сохраняет их attempts отдельно. Сам workflow не скачивает чужие/предыдущие runs и не требует `actions: read`. Legacy run без telemetry обозначается missing. Частота failure даётся как `failed / observed`, missing/skipped выводятся отдельно; разные SHA/runner не сливаются в доказательство flakiness. Смена fail/pass на одном checkout tree + environment fingerprint — только кандидат на нестабильность, даже для rerun. При n<10 не выводить p95 как устойчивую оценку; показывать n, median и max. Это не автоматический мониторинг следующих десяти запусков.

#### 6.2.3 Единственное исполнение тяжёлых BDD-контрактов

Первый ограниченный пакет — server CRUD/realtime, emoji filter и search. Не распространять автоматически на все `Storm*`.

Выбранный дизайн: существующий executable BDD scenario остаётся каноническим CI entry point. Он реально выполняет все связанные действия/assertions через helpers. Полностью дублирующие самостоятельные entry points перестают обнаруживаться как отдельные TUnit tests; тела переносить в scenario helpers, **не оставлять ручное создание test class и вызовы его test methods**. Уникальные самостоятельные проверки остаются обнаруживаемыми.

Для server BDD сохраняет оба live-контракта и свои дополнительные API/feature/step assertions. Один запуск каждого live-контракта по-прежнему поднимает собственный host/DB; совместное использование mutable RavenDB между тестами не входит в эту оптимизацию. Для emoji сохраняются все пять проверок из `EmojiFilterUiContract`, для search — обе из `SearchBehaviorUiContract`. Roadmap search дополнительно вызывается из `RoadmapInteractionsContract`: этот caller тоже переводится на helper, но отдельный Roadmap BDD scenario сохраняется. «Один раз» относится к одному canonical scenario; общая часть двух разных живых сценариев не устраняется кешированием результата. В coverage map и invocation checks явно перечислить оба потребителя.

Перед удалением discovery каждого дубля создать `docs/testing/ci-test-coverage-map.md`: old test ID -> canonical scenario/helper -> assertions/data variants -> replacement filter. Право убрать дубль появляется только при полном совпадении поведения, данных, setup и очистки либо при явном сохранении различий отдельным test case. Нельзя заменять живой BDD проверкой наличия строки/тега. Feature/scenario/step IDs и trace исполнения сохраняются; scenario results не кешировать между тестами. В существующем `docs/product/storm.json` обновить активные path/symbol/command и связанные derived mappings затронутых TS; IDs не менять, исторические записи evidence с датами и прежними командами не переписывать. Проверить, что активные ссылки указывают на обнаруживаемый BDD entry/helper, а historical evidence явно остаётся историей.

После удаления самостоятельных entry points не терять независимость failure paths: ранее независимые helpers внутри BDD-пакета исполняются с собственными fixture/cleanup и subcase outcome даже после обычного assertion failure другого helper. Общий scenario завершать failed с агрегированными исходными ошибками/стеками; результат failed subcase не подменять boolean success. Cancellation, fatal host failure или недоказанно завершённый cleanup останавливают небезопасное продолжение: оставшиеся subcases явно not-executed, scenario не green. Настоящие последовательные шаги одного stateful flow не продолжать после разрушения предусловий; это правило касается объединения ранее независимых тестов, а не всех Gherkin steps.

Добавить узкую контрактную проверку discovery/coverage map для этого пакета: каждый обязательный сценарий/вариант покрыт, не потерян при снятии `[Test]`; счётчик invocation подтверждает один запуск каждого тяжёлого helper в выбранном наборе. Проверить stub-cases: первый независимый helper fails, остальные выполняются, cleanup завершён, итог red; cancellation/cleanup failure не поглощаются. Новая детерминированная emoji-регрессия из §6.2.6 остаётся отдельным тестом: она проверяет управляемое пересечение событий, а не дублирует обычный happy path. Изменение числа обнаруженных тестов допустимо только с объяснённой картой, не с жёстким старым числом 1247.

#### 6.2.4 Минимальные UI-fixtures: пилот

Начальный scope: `BreadcrumbEmojiUiTests.Breadcrumbs_ShouldRenderEmojiRunsWithEmojiFont` и два узких теста `MainControlTaskCardLayoutUiTests`: `CurrentTaskCard_PlanningDatePickers_UseDurationFieldPadding`, `CurrentTaskCard_DarkTheme_UsesThemeAwareAccentButtonChrome`.

Выделить минимальный test-only builder нужного control/template с реальными Avalonia styles/resources и минимальным in-memory data context, без 26 файлов, FileSystemWatcher и полного MainControl. Если тест доказывает именно связь с MainControl, сохранить его как один integration entry point, а узкие style assertions выделить отдельно; не заменять реальный control его копией/XAML imitation. Полный `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls` остаётся интеграционной страховкой. Для breadcrumb сохранить проверку привязки к выбранной задаче/иерархии хотя бы в одном полном UI-сценарии, не только проверку шрифта вручную созданного TextBlock.

Пилот принимается только после карты assertions и значимого ускорения. Если выделение control требует production redesign либо выигрыш исчезает после прогрева — оставить текущий тест и отметить гипотезу отклонённой. Массовая замена всех fixtures не входит в пакет.

Добавить Activity/Stopwatch spans `setup`, `body`, `cleanup` на пилотах, трёх fault matrices и двух live helpers. Фаза ожидания resource lock отмечается отдельно, не вычитается молча из latency. Awaitable teardown, producer seal, drain pending writes, dispose storage/config и удаление только собственной temp directory сохраняются. Глобально общая mutable fixture запрещена; shared Avalonia session не вводится этим этапом.

#### 6.2.5 Матрицы отказов и реальная изоляция

Три `TaskSpaceTransactionTests`: ActivationProjection, CatalogMutation и FirstMigration с суффиксом `EveryPersistedWriteFault` сохраняют **все** записи/cutpoints, реальные файлы, reopen/recovery и assertions полного before/after state.

Выделить случай в helper с identity `(scenario, operation, faultIndex)`, отдельными setup/write/recovery timings и failure output. Если перечень доступен на discovery без side effects — использовать параметризованные TUnit cases. Если перечень определяется recording-прогоном, оставить управляемый loop с отдельным case trace и итоговым coverage manifest: `recordedFaultCount`, `executedFaultIndices`, `passedFaultIndices`. Не запускать файловую подготовку из discovery и не кодировать вчерашнее число записей константой.

Оптимизация: один раз подготовить immutable seed для сценария, затем независимая **физическая копия** в уникальную temp directory для каждого cutpoint; без hardlinks на mutable файлы. Seed не содержит активных handles/watchers/путей другой fixture. Не кешировать результат операции, fault injection или recovery. При изменении числа записей новые точки автоматически входят в manifest. При отсутствии выигрыша оставить исходную подготовку и новые диагностические имена.

Изоляция: сначала минимальный воспроизводящий probe TUnit с теми же attributes, runner/args; затем карта resource users (`Avalonia/ReactiveUI/global registry`, server host globals, независимые disk cases). Все тесты, действительно меняющие один глобальный ресурс, должны иметь один общий constraint key или доказанный общий limiter. Не связывать независимые ресурсы из-за похожего названия класса. В accepted trace одновременные critical sections одного ресурса отсутствуют, независимые тесты могут перекрываться. В telemetry сохранять фактические test-body интервалы; не называть набор serial по одному аргументу CLI. Устранение конкретного нарушения допустимо правкой test attributes/hooks; смена версии runner или отдельные process shards требуют пересмотра spec.

#### 6.2.6 Emoji: RED/GREEN и узкая граница production fix

Воспроизвести пересечение `AllEmojiFilter.ShowTasks` / include-exclude переключения с поступлением обновлений task source и обработкой `CollectionChanged`. Использовать controlled scheduler/barriers и явное ожидание завершения UI операции, а не случайный sleep. Снять thread ID, последовательность changesets/collection notifications и выбранный элемент до/после — на синтетических данных, без dump пользовательских задач.

Кандидаты, которые надо различить: неправильный scheduler перед binding, реентерабельная модификация коллекции, invalid selection/container index при bulk update. Сейчас ни один не доказан. Регрессия должна воспроизвести тот же класс нарушения (index/collection consistency), а не искусственно бросить исключение из fake.

Допустимый production scope: `src/Unlimotion.ViewModel/MainWindowViewModel.cs` — subscriptions массового переключения и связанные UI-проекции; `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml.cs` — только безопасная реакция выбора/переключения. Выбрать минимальный фикс по полученному trace: serialized UI notifications, устранение reentrancy или корректное восстановление selection. Не считать `ObserveOn`/`Edit` автоматически достаточным; не добавлять dispatcher к файловым/долгим операциям целиком. Не менять правила включения/исключения, reset, text search, popup persistence и клавиатурное управление.

Если проблема не воспроизведена за ограниченную серию §11, сохранить диагностические findings и mark emoji этап incomplete. Не делать спекулятивный production patch и не выдавать 100 green repeats за доказанную починку.

Visual planning artifact: новый layout не нужен; текстовый storyboard ниже задаёт неизменный видимый контракт и является fallback вместо wireframe:

`Открытый список emoji -> Space/клик на All -> все доступные элементы получают согласованное состояние, popup открыт -> приходит обновление задач -> список и selection остаются валидны -> повторный Space переключает All обратно -> Escape закрывает popup, фокус возвращается предсказуемо`.

Для UI fix нужны `до` failing и `после` passing artifacts автоматизированного сценария. Если headless harness не поддерживает безопасное видео, документировать техническую причину, точную команду и next-best evidence: RED/GREEN log, notification trace и снятые headless screenshots, если поддержаны. Существующий FlaUI можно использовать при доступном harness, но создание нового recorder не входит в scope. Не называть обычный screenshot видео или green happy path воспроизведением дефекта.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| U1 | Разработчик открывает завершённый run | Два project outcome, top slow, failure kind, TRX/HTML доступны | Summary, artifact inventory | AC1–AC3 |
| U2 | Main assertion падает | Headless всё равно запускается; `All tests` красный | Оркестрационный failure-path check | AC1 |
| U3 | Headless не компилируется | Ошибка видна до Main test; Main всё равно проверяется | Build/test timestamps | AC1 |
| U4 | Разработчик выбирает старый дублирующий тест | Coverage map даёт работающий replacement filter, assertions сохранены | Discovery + helper invocation trace | AC4 |
| U5 | Пользователь массово переключает emoji при обновлении задач | Нет exception, корректные фильтры/selection/popup/keyboard | RED/GREEN UI regression | AC7 |
| U6 | Разработчик сравнивает скорость | Сопоставимые baseline/candidate, без обещания выигрыша из разных SHA/машин | Paired benchmark report | AC5, AC6, AC9 |
| U7 | Тестовый run оборван или отчёт отсутствует | Incomplete/cancelled; отсутствие данных не маскируется green | Reporter negative fixtures | AC2, AC3 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Checkout/SDK готовы | Один restore/build fails | Другой проект продолжает собственную цепочку | Setup failure блокирует оба | Не скрывать exit code |
| Main built | Main test fails | Headless runs при своём успешном build | Timeout/cancel прерывает job | Red остаётся red |
| Test completed | TRX/HTML отсутствует | Telemetry error | После build failure — not-executed | Не ноль успешных тестов |
| Report partially present | Cancellation | Best-effort upload, incomplete metadata | Runner loss может не дать upload | Не гарантировать недоступное |
| Fixture active | Pending write + cleanup | Seal -> drain -> dispose -> delete | Повторный cleanup ждёт тот же результат | Старый lifecycle contract |
| Emoji popup открыт | All + source update | Согласованная коллекция/selection на UI thread | Empty list, add/remove selected item | Не терять keyboard focus |
| Fault case создан | N-я persisted write fails | Reopen даёт целостное ожидаемое состояние | Каждый N, отдельная physical copy | Не уменьшать матрицу |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| База | agent | Текущий f39b3245; Feed не переносить | 0.98 | Смешение несвязанных feature commits | Нет |
| CI topology | agent | Один job, независимые step conditions и ранние build | 0.95 | Shared timeout остаётся | Нет |
| BDD dedup | agent | Реальный BDD — canonical entry, карта удалённых дублей | 0.92 | Потеря старого фильтра/variant без карты | Нет |
| UI-fixture scope | agent | Три названных пилота, integration страховка | 0.95 | Широкий refactor дороже выигрыша | Нет |
| Perf budget | agent | Проверяемые пороги §11, откат неэффективных изменений | 0.90 | Шум hosted runner | Нет |
| Emoji patch | agent | Только после доказанного RED, иначе incomplete | 0.99 | Ложный claim fix | Нет |
| Artifact lifetime | agent | 14 дней, allowlist отчётов, без secrets | 0.95 | Ограниченная глубина доступной истории | Нет |
| GitHub delivery | user | Вне этой spec; нужна отдельная авторизация отправки | 1.00 | Неавторизованная публикация | Нет, локальный EXEC не зависит от push |

Approval самой spec оформляется в конце, это не нерешённый выбор дизайна.

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Required-check identity | Workflow `all-tests` / `All tests` | Имена и triggers без изменений | GitHub settings не трогать | YAML contract check |
| Test runtime | global.json, Package props/csproj | Те же версии, отдельный build | No SDK migration | metadata + build |
| Results | TRX/HTML, step outcomes | schemaVersion=1 JSON + summary | Legacy reports missing, raw originals intact | Reporter fixtures |
| Shared state | Hooks/attributes/fixtures | Общий constraint для фактического ресурса | Без новых process-global caches | critical-section trace |
| Persistent user data | Production storages | Без изменений | Миграция не нужна | Diff scope + recovery tests |
| Daily Feed | Другая ветка с двумя fix commits | Только сохранить fixes при integration | Нет cherry-pick feature | Условная проверка двух методов |

## 7. Бизнес-правила / Алгоритмы

1. Failed build, test assertion failure, runner crash, cancellation, zero-discovery и telemetry error — разные статусы.
2. Gate green требует оба проекта реально выполненными, не skipped; report failure не снимает первичную ошибку.
3. `failed/observed` — описательная статистика; flaky label требует исследования смены результата при сопоставимом коде/окружении.
4. Duplicate removal разрешено только при сохранении каждой unique assertion и data variant.
5. Каждый faultIndex должен быть исполнен и иметь результат; пропуски — failure coverage contract.
6. Никакой UI-bound mutable state не меняется конкурентно из двух critical sections одного ресурса.
7. Test cleanup не удаляет файлы до завершения принадлежащих fixture producers/handles.

## 8. Точки интеграции и триггеры

- `push main`, `pull_request main`, `workflow_dispatch` остаются triggers.
- Успех собственного build разрешает test step, а completion test step запускает сбор его результатов.
- TUnit test lifecycle/hooks производят phase trace; reporter соединяет его с execution identity без двойного подсчёта nested BDD helpers как новых TUnit tests.
- BDD step definitions вызывают scenario helpers, не test methods.
- `AllEmojiFilter` subscriptions и user input control — единственные разрешённые production точки исправления при доказанной причинной связи.

## 9. Изменения модели данных / состояния

Production persisted model не меняется. Новые данные — schemaVersion=1 test reports, coverage map и transient diagnostic spans. Trace содержит project/test/case/resource IDs, monotonic durations, UTC для привязки, thread ID; не содержит токены/полные пользовательские документы. Seed fault fixture — временный immutable source, удаляемый после завершения принадлежащих ему случаев.

## 10. Миграция / Rollout / Rollback

- Первый этап даёт telemetry и ранний build, сохраняя check name. Старые runs не переинтерпретируются как новые измерения.
- Далее dedup, fixture pilot, fault optimization и emoji fix внедряются раздельными логическими change sets; при будущей Git delivery — отдельными Conventional Commits.
- Неэффективный perf change откатывается отдельно; telemetry и новые полезные regressions сохраняются, если сами не создают проблему.
- Rollback CI возвращает прежний workflow; runtime/user data migration отсутствует. Откат dedup возвращает removed entry points вместе с coverage map, без изменения scenario semantics.
- После разрешённого push live CI проверяется на конкретном SHA. Без этого можно завершить локальную реализацию с соответствующим ограничением, но не утверждать «GitHub CI проверен» или закрывать remote часть AC1–AC3.
- Feed regression проверяется только если на будущем integration head уже есть эти классы: 10 отдельных запусков каждого метода. На базовом f39b3245 отсутствие Feed — ожидаемо, не тестовый skip, скрывающий дефект.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **AC1 — orchestration:** имена/triggers/permissions сохранены; оба build предшествуют tests; failed Main не мешает Headless; failed Headless build не мешает Main; любой failed prerequisite/test оставляет red. Проверены обычный success и отрицательные ветки, включая zero discovery.
- **AC2 — artifacts:** на успешно завершённом run оба проекта имеют TRX, HTML, metadata, normalized JSON, summary; на failure доступны все реально созданные outputs плюс точный not-executed/incomplete status. TRX/HTML не требуются от не стартовавшего test host; missing успешного результата — error. Невозможность upload при runner loss явно оговорена.
- **AC3 — report correctness:** корректные durations/identity/attempts, escaping, missing/cancelled distinction, top slow и denominator; fixtures с одинаковыми display names и разными arguments, build failure, malformed TRX, отсутствием HTML и legacy history проходят reporter checks. Несопоставимые runs не объявлены доказательством flake.
- **AC4 — coverage:** три BDD-пакета всё ещё реально выполняют feature steps; unique assertions и variants сохранены; removed entry points перечислены; каждый дублировавшийся heavy helper выполняется ровно один раз в соответствующем пакете; replacement filters discover >0. Assertion failure первого независимого helper не скрывает результаты остальных; cancellation/неуспешный cleanup видимы, aggregate scenario остаётся red.
- **AC5 — UI fixture:** assertions трёх пилотов и полные integration страховки проходят; lifecycle regressions остаются green; retained optimization даёт порог §11 без новых leaked directories/unobserved tasks.
- **AC6 — fault matrices:** одинаковое множество записей/faultIndex до/после, все cases проверяют реальные persisted файлы и recovery. Новая диагностика видна; seed optimization остаётся только при значимом выигрыше.
- **AC7 — emoji:** deterministic expected RED на baseline и GREEN на candidate; 100 последовательных отдельных процессов без failure, затем affected UI class/BDD и full runs. Покрыты include/exclude, All on/off, empty/add/remove, keyboard/popup/selection. Trace объясняет причину; production diff только в разрешённых точках.
- **AC8 — isolation:** минимальный runner probe сохранён; users каждого shared resource учтены; trace трёх полных candidate runs не показывает overlap critical sections одного ресурса. Нет утверждения о полной serial execution без trace.
- **AC9 — performance:** paired before/after report содержит environment fingerprint, exact commands, warmup, raw samples, mean/median/max, setup/body/cleanup и wall-clock. Неэффективные оптимизации откатаны, неизвестный общий выигрыш не выдан за достигнутый.
- **AC10 — completion:** affected build и оба полных CI-safe проекта green; нет unobserved/teardown failure, неоговорённых missing tests или unrelated diff. GitHub validation отдельно помечена выполненной либо pending delivery. Feed compatibility — по применимости §10.

### Измерения и пороги

Исторические 22:42 на Daily Feed — ориентир, не baseline для текущей ветки. Для каждого пакета брать одинаковый source snapshot за исключением рассматриваемого изменения, одну машину, Debug, тот же SDK/runtime/config/args, отсутствие посторонних builds. Сначала отдельный build, затем один warmup; чередовать baseline/candidate, не смешивать compilation и test duration.

- Targeted optimization: 5 измеряемых запусков каждого варианта отдельными процессами; значимый выигрыш — снижение median не менее 15% и минимум 0.5 с для сравниваемого пакета. Для dedup сравнивается суммарная работа пакета при одинаковом наборе assertions, не длительность переименованного wrapper.
- Full-suite: 3 измеряемых последовательных прогона каждого варианта обоих проектов на одной машине, после сборки. Сравнивать медиану суммарного последовательного wall-clock, отдельно CPU/allocated bytes при надёжной возможности измерения. Если allocations не получены — явно N/A, без выдуманных цифр.
- Ориентир пакета — 10% снижения full-suite median, но без baseline это гипотеза, не обязательная квота. Приёмка performance требует значимого targeted выигрыша хотя бы одного из разрешённых пакетов и отсутствия необъяснённого full-suite ухудшения; фактический общий процент сообщается, даже если он меньше 10%. Нельзя расширять scope или ослаблять coverage ради круглой цифры. Если ни одна оптимизация не даёт значимого выигрыша, perf часть остаётся без доказанного результата, telemetry/bug fix оцениваются отдельно.
- Regression guard: необъяснённое замедление full-suite более 5% либо рост teardown failures блокирует принятие оптимизации; корректность/bug fix оценивается отдельно от perf change. Стоимость необходимой изоляции измеряется отдельно и не скрывается за выигрышем dedup.
- При шуме допускается один расширенный цикл до 10 targeted / 5 full samples на вариант. Далее не бесконечные перезапуски: report inconclusive и диагностика окружения.

Эти серии — validation EXEC, не постоянное умножение тестов на каждом PR. Стоимость full before/after ожидаемо несколько часов; перед длинной командой сообщить пользователю объём/ожидаемую длительность и путь к progress log. Не повышать command timeout после зависания без progress/root-cause inspection.

### Команды / запуск

Команды ниже для будущего EXEC. На SPEC они не выполнялись. Новые scripts — целевой интерфейс, не уже существующая возможность.

```powershell
dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false
dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug -p:UseSharedCompilation=false

dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --maximum-parallel-tests 1 --output Detailed --report-trx --results-directory artifacts/test-results/local-main
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build --no-restore -- --maximum-parallel-tests 1 --output Detailed --report-trx --results-directory artifacts/test-results/local-headless

# До dedup — исходный entry point; после — новый deterministic case и replacement filters из coverage map.
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter" --maximum-parallel-tests 1 --output Detailed --report-trx --results-directory artifacts/test-results/emoji-baseline

pwsh -File scripts/ci/Test-TestReport.ps1
pwsh -File scripts/ci/Write-TestReport.ps1 -ResultsRoot artifacts/test-results -OutputRoot artifacts/test-analysis
git diff --check
```

Каждый повтор использует новый output directory. Series script принимает `-Project`, `-TreeNodeFilter`, `-Repeat`, `-OutputRoot`; expected discovered count проверяется по отчёту, 0 — invocation failure. Сначала dry discovery/одиночный запуск, затем серия. Test timeout не превращается в skip, отменённая серия не считается 100/100.

Для оркестрации нужны script/YAML contract checks и контролируемый запуск со stub runner, который возвращает success/failure/zero-results для каждого project и записывает порядок вызовов. Stub включается только локальным test harness, не произвольным публичным workflow input. Это проверяет failure propagation; actual GitHub `if`/artifact wiring дополнительно проверяется live run после отдельной авторизации Git delivery. Локальный harness не объявляется эквивалентом GitHub.

Staged validation: characterization/RED -> targeted -> affected UI/BDD classes -> build -> оба full suites -> paired measurements -> post-EXEC review. Если emoji не воспроизводится после controlled schedules и одной серии до 100 отдельных запусков, дальнейший production fix блокируется; остальные независимые этапы продолжаются.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | Orchestration success/Main fail/Headless build fail/setup fail/cancel/zero checks | Workflow conditions + live run outcomes | orchestration-results, CI URL | Live pending до разрешённого push |
| AC2 | Missing/output allowlist/upload configuration checks | Скачать и открыть оба report artifacts | artifact inventory, run.json | Runner loss: best effort |
| AC3 | Reporter malformed/missing/identity/attempt/history fixtures | Summary не смешивает источники | reporter-results, summary.md | Нет |
| AC4 | BDD feature execution + discovery + invocation count | Все старые assertions/variants сопоставлены | coverage map, trace | Нет |
| AC5 | Три пилота + integration UI + fixture lifecycle regression | Сравнить реальные controls/styles | targeted TRX, phase spans | Возможен отказ от гипотезы с доказательством |
| AC6 | Все три fault matrices | Равенство case sets и real file recovery | fault-cases.json, TRX | Seed optimization необязательна без выигрыша |
| AC7 | Controlled race RED/GREEN + 100 process repeats + affected UI | Storyboard, trace, video либо обоснованный fallback | emoji-before/after results | Невоспроизводимость означает incomplete, не PASS |
| AC8 | Shared-resource probe + instrumentation | Нет overlap одинакового ресурса в 3 full runs | concurrency trace | Нет |
| AC9 | Paired targeted/full series | Пороги и equivalence of assertions | performance-before-after.md, raw samples | Inconclusive явно не успех |
| AC10 | Build + оба full green; Feed repeats при наличии | Diff scope, zero leaks, pending remote отделён | validation-summary.md | Feed N/A на f39b3245 |

## 12. Риски и edge cases

- UI notification bug может лежать вне разрешённого production scope: нужен новый design review вместо локального симптоматического catch.
- Снятие `[Test]` меняет фильтры и число тестов: replacement mapping и executable BDD обязательны; старое имя не обещается как совместимый CLI API.
- HTML schema TUnit может отличаться: raw HTML сохраняется, normalized results опираются на TRX/явную metadata; parser не зависит от minified JavaScript.
- Более строгая изоляция может увеличить wall-clock: это correctness fix, его стоимость явно отделяется от acceleration; цель не достигается отключением limiter.
- Нельзя принять «после faster», если baseline был cold/другая ветка/машина. Full comparison дороже трёх одиночных повторов и планируется заранее.
- Один job может отмениться между Main и Headless: cancellation видим, запуск не green. Изоляцию runners в этой версии не обещаем.
- Seed может сохранить абсолютные пути или чужое владение handle: отдельная physical copy, fresh config instances и test-owned cleanup.
- Retention 14 дней не гарантирует наличие десяти runs при редких запусках; показывать реальный размер истории, не расширять доступ автоматически.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Ускорение получено удалением тестов? | BDD dedup уменьшит discovery count | Карта всех assertions/variants и реальное исполнение feature steps | mitigated |
| Почему снова исправляем Daily Feed? | Два failure из аудита уже green | Только compatibility check при наличии, без feature backport | mitigated |
| Есть обещание конкретного ускорения без baseline? | Исходные runs имеют разные SHA/count | Пороги, paired baseline, stop при inconclusive; нет прогнозного процента результата | mitigated |
| Emoji опять просто прошёл несколько раз? | Уже было 10/10 без repro | Обязательный controlled RED/GREEN, 100 repeats лишь дополнение | mitigated |
| Почему не отдельный Headless job? | Он мог бы работать параллельно | Сохранён check и один runner; shared timeout явно признан ограничением | accepted-risk |
| Отчёт доступен только на машине агента? | Старый audit в ignored artifacts | Snapshot в spec, будущие результаты upload + CI summary | mitigated |

### Rework Prevention Checklist

- Пользователь видит два project status, top slow, downloadable reports и устойчивый emoji UI; U1–U7 перечислены.
- Evidence для каждого сценария и AC указан; локальные результаты отделены от GitHub validation.
- Agent-owned решения и ограничения зафиксированы, блокирующего продуктового выбора нет.
- Уникальное покрытие, lifecycle и работа с другим baseline защищены явными контрактами.
- Role-based review и findings фиксируются в §19 до запроса approval.
- AC проверяют результат, а не факт намерения запустить тесты.
- EXEC имеет bounded validation path; неизвестная emoji-причина не превращается в обещание фикса.

## 13. План выполнения

1. **Достоверные измерения:** preflight текущего HEAD/dirty state, baseline discovery и runs; report schema, exporter, reporter tests; CI build/step conditions/upload. До perf изменений иметь воспроизводимый baseline.
2. **Изоляция и emoji reproduction:** minimal runner probe, resource map, deterministic failing UI case. Начать диагностику до dedup, чтобы сохранить исходный reproduction path; не блокировать независимую telemetry работу при отсутствии repro.
3. **Без повторной работы:** coverage map и три ограниченных BDD-пакета, targeted validation и paired timings. Карта precedes removal entry points.
4. **Недорогая подготовка:** три UI-пилота и три fault matrices; по одной гипотезе, отдельный замер/решение keep/revert. Не расширять на остальные классы без review.
5. **Устранение подтверждённой гонки:** узкий production patch после RED, 100 repeats, UI scenario evidence; отсутствие RED — incomplete этого этапа.
6. **Приёмка:** affected builds, full green обоих проектов, 3 paired full samples, report против AC, post-EXEC review. При разрешённой delivery — конкретный live CI run и проверка artifacts; без неё указать pending remote validation.

## 14. Открытые вопросы

Нет блокирующего выбора для утверждения плана. Точная причина emoji failure, реальная семантика установленного limiter и величина ускорения — исследовательские результаты EXEC с ограниченными ветками действий и stop rules, а не неизвестные продуктовые требования. Публикация на GitHub остаётся отдельным действием, не скрытым условием локального начала реализации.

## 15. Соответствие профилю

- Stack: central `creator-vibe-lens`, `model-behavior-baseline`, `tool-execution-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`; context `performance-optimization`; profiles `dotnet-desktop-client` и `ui-automation-testing`; overlays `refactoring-policy`, `spec-linter`, `spec-rubric`, `review-loops`; local `AGENTS.override.md`.
- Creative skill не применялся: задача техническая, требования происходят из аудита.
- UI-thread не блокируется длительными операциями; selectors и visible semantics сохраняются; UI regression обязательна для fix.
- Spec задаёт visual storyboard, video/fallback contract и staged validation. Тесты не запускаются заново на SPEC, поскольку здесь изменяется только документ.
- Каждая perf гипотеза имеет измерение/порог/risk/rollback; полный до/после прогон запланирован, старый аудит не подменяет candidate evidence.

## 16. Таблица изменений файлов

Пути — разрешённая поверхность будущего EXEC, не список уже сделанных правок.

| Файл | Изменения | Причина |
| --- | --- | --- |
| Эта spec | Дизайн, review и журнал | Единственная мутация SPEC |
| `.github/workflows/tests.yml` | Ранние независимые build/test, отчёты | CI completeness |
| `scripts/ci/Write-TestReport.ps1`, `Test-TestReport.ps1`, `Invoke-TargetedTestSeries.ps1` | Новые reporting/validation helpers | Повторяемая диагностика |
| `scripts/ci/Invoke-TestStage.ps1` | Общий native invocation/exit metadata helper для отдельных workflow steps | Проверять реальное распространение exit codes без копии orchestration в тесте |
| `scripts/ci/Test-TestOrchestration.ps1` (новый), test fixtures рядом | Проверка отрицательных веток и ordering | Не потерять exit/failure propagation |
| `docs/testing/ci-test-coverage-map.md`, `docs/testing/ci-test-analysis.md` (новые) | Assertions mapping, команды, schema, отчёт/ограничения | Воспроизводимость и смена filters |
| `docs/product/storm.json` | Только активные ссылки/derived mappings затронутых TS и новые dated evidence; старую историю сохранить | BDD registry не должен указывать на удалённый entry point |
| `src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs`, `ServerStorageCrudRealtimeContract.cs`, `StormServerStorageCrudRealtimeExecutableSpecTests.cs` | Только dedup и phase trace | Убрать два повторных live исполнения |
| `src/Unlimotion.Test/EmojiFilterUiContract.cs`, `SearchBehaviorUiContract.cs`, соответствующие два `Storm*ExecutableSpecTests.cs`, `StormBdd/EmojiFilterStepDefinitions.cs`, `StormBdd/SearchBehaviorStepDefinitions.cs`, `StormBdd/ServerStorageAuthStepDefinitions.cs` | Helpers вместо ручных test calls, сохранить steps | BDD traceability |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | Перенос пяти duplicate bodies, controlled regression | Emoji correctness |
| `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `RoadmapGraphUiTests.cs` | Два search duplicate bodies/entry points; EXEC: готовность persisted CompletedTree fixture перед lazy UI subscription | Ограниченный scope; одинаковая readiness preparation обоим вариантам после диагностированного full-run зависания |
| `src/Unlimotion.Test/RoadmapInteractionsContract.cs` | Только перевод общего search caller на helper | Не сломать второй BDD consumer |
| `src/Unlimotion.Test/BreadcrumbEmojiUiTests.cs`, `MainControlTaskCardLayoutUiTests.cs` | Только три названных пилота + integration страховка | Fixture cost |
| `src/Unlimotion.Test/WorkspaceBreadcrumbsLastOpenedUiContract.cs` | Добавить emoji в существующие parent/child titles и path assertion | Сохранить full-window emoji breadcrumb binding при облегчении renderer pilot |
| `src/Unlimotion.Test/CiReadmeMediaContract.cs` | EXEC: проверить project/parallelism в вызываемом CI helper, делегирование в YAML | Устранить устаревшую static assertion после согласованного переноса orchestration; loading UI smoke сохранить |
| `src/Unlimotion.Test/TaskSpaceTransactionTests.cs` | Три fault matrices, IDs/seed/trace | Recovery cost/diagnostics |
| `src/Unlimotion.Test/TestParallelLimits.cs`, `ReactiveUiSessionHooks.cs`, узкие attributes затронутых resource users | Проверенный ресурсный constraint | Реальная изоляция |
| Новые test-only scenario helpers, minimal UI builder, telemetry/probe/coverage checks внутри `src/Unlimotion.Test` | Только контракты §6 | Не создавать общий framework |
| `MainWindowViewModelFixture.cs`, `HeadlessSessionExtensions.cs`; `tests/Unlimotion.UiTests.Headless/Infrastructure/HeadlessSessionHooks.cs` | Только нужная instrumentation/constraint, не переписывать cleanup | Сохранить lifecycle |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml.cs` | Условный узкий fix после RED | Единственный production scope |

Feature-файлы, product storage, package versions и все прочие production files не менять. Если перенос BDD entry points требует обновления имеющегося traceability registry, обновить только ссылки на test identity без изменения feature/step semantics и перечислить это в review. Новые широкие abstraction layers или переработка всех UI tests требуют новой spec review.

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| CI evidence | Suite totals, нет uploaded reports | TRX/HTML + metadata + individual timings |
| Headless | Зависит от успешности Main | Зависит только от своих prerequisites и жизни job |
| BDD | Вызов уже обнаруженных test methods | Один executable owner, helpers, assertion map |
| UI-fixtures | Полный storage/window даже для narrow style checks | Измеренный минимальный pilot с integration страховкой |
| Fault matrices | Один долгий результат скрывает отдельные случаи | Named case trace, complete coverage manifest |
| Emoji | Один исторический сбой, green repeats | Доказанная регрессия/фикс либо честный incomplete |
| Parallelism | Доверие CLI-флагу | Resource contract + наблюдаемые spans |

## 18. Альтернативы и компромиссы

- **Отдельные CI jobs:** Headless быстрее даёт независимый результат, но добавляет runner/build/cache cost и требует aggregate check. Отложено: независимые steps решают пропуск после failure без смены delivery-контракта; shared timeout принят явно.
- **BDD только как статический lint:** быстрее и сохраняет теги, но перестаёт проверять исполнение feature steps. Отклонено; выбран executable BDD с удалением доказанных duplicate entry points.
- **Одна fixture/DB на весь suite:** может ускорить, но создаёт зависимость от порядка/остаточного состояния. Отклонено; общий только immutable seed, каждый test владеет своим состоянием.
- **Сразу глобальная сериализация:** снижает часть конфликтов, но не доказывает устранение lifecycle/reentrancy и может замедлить suite. Сначала probe и ресурсные constraints.
- **Большие retry/timeout:** создают ложный green и увеличивают время. Отклонено; диагностика и controlled schedule.
- **Сначала все оптимизации, затем отчёт:** уменьшает начальный объём tooling, но не позволяет честно подтвердить эффект. Сначала telemetry/baseline.

## 19. Результат quality gate и review

### SPEC Linter Result

Проверка выполнена по central `spec-linter`, каждый пункт оценён отдельно. Это проверка дизайна, не результат будущих тестов.

| Блок | Пункт | Статус | Комментарий |
| --- | --- | --- | --- |
| A | 1. Цель | PASS | §1: измеримый CI/testing outcome и граница SPEC |
| A | 2. AS-IS | PASS | §2: 10 CI runs, разные SHA, raw evidence и пределы вывода |
| A | 3. Проблема | PASS | §3: отсутствие измеримого execution/isolation контракта |
| A | 4. Цели дизайна | PASS | §4: ownership, coverage, измеримость |
| A | 5. Non-Goals | PASS | §5: без Feed backport, retry, package migration и delivery |
| B | 6. Ответственность/дизайн | PARTIAL | Компоненты и границы определены; точный emoji patch зависит от controlled RED, unsafe guess запрещён |
| B | 7. Интеграции | PASS | §8, оба потребителя Roadmap search, active STORM pointers |
| B | 8. Правила | PASS | §7 и state matrix различают failure classes/coverage |
| B | 9. Ошибки | PASS | §6: native exit, incomplete artifacts, subcase failure, cancellation |
| B | 10. Производительность | PASS | §11: paired samples, warmup, пороги и bounded inconclusive path |
| C | 11. Данные | PASS | §9: production persisted model без изменений |
| C | 12. Миграция/совместимость | PASS | §10: прежний check, версии, BDD IDs, historical evidence |
| C | 13. Rollback | PASS | Независимые change sets и откат неэффективного perf change |
| D | 14. AC | PASS | AC1–AC10 проверяют outputs/behavior, включая negative paths |
| D | 15. Test plan | PASS | Матрица AC, RED/GREEN, full suites, lifecycle и faults |
| D | 16. Команды | PASS | Проверенные TUnit формы; новые scripts явно обозначены как будущие |
| E | 17. Этапы | PASS | §13: telemetry/repro до удаления entry points и perf patches |
| E | 18. Вопросы/stop rules | PASS | Нет неизвестного продуктового выбора; research incomplete явно отделён |
| E | 19. Масштаб | PASS | Large; named pilots/contracts, один job, два production файла условно |
| F | 20. Профили | PASS | UI regression/storyboard/evidence, performance до/после, QUEST gate |

Итог: **ГОТОВО к утверждению плана**. PARTIAL означает неизвестную причину emoji-сбоя, а не разрешение на недоказанный фикс.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Названы outputs, entry points, conditional production scope и non-goals |
| 2. Понимание текущего состояния | 5 | CI audit, реальные callers/registry, lifecycle и baseline distinction |
| 3. Конкретность целевого дизайна | 2 | CI/BDD/report определены; emoji causal patch намеренно не предрешён |
| 4. Безопасность (миграция, откат) | 5 | User data не меняются, assertions/IDs сохраняются, есть rollback |
| 5. Тестируемость | 5 | AC-to-test mapping, отрицательные ветки, bounded RED/GREEN и paired samples |
| 6. Готовность к автономной реализации | 5 | Порядок, команды, stop/partial boundaries, design choices зафиксированы |

Итоговый балл: **27 / 30**. Зона: готово к автономному выполнению **после approval**, с обязательной диагностической стадией emoji. Оценка не гарантирует воспроизведение гонки или заданный процент ускорения.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable: developer CI workflow | Получит ли разработчик полную проверку без потери сценариев? | PASS | Уточнены independent subcase outcomes и separate delivery state |
| UX / designer | applicable: emoji и test controls | Сохраняются ли popup, focus, selection, styles? | PASS | Storyboard и real-control integration страховка; redesign не нужен |
| Tester / validation | applicable | Отрицательные ветки, baseline и число проверок доказуемы? | PASS | Исправлены artifact-on-failure и BDD fail-fast; 10% не фиктивная квота |
| Developer / architect | applicable | Сохраняются ли ownership, callers и уникальные assertions? | PASS | Roadmap second caller и registry добавлены в scope; shared mutable fixtures исключены |
| Delivery / operations / security | applicable | Сохранены ли check/exit/permissions и безопасные artifacts? | PASS | Явные native exit codes, upload allowlist, remote validation отдельно |

### Post-SPEC Review

- Статус: PASS после исправлений, при известных research risks ниже.
- Scope reviewed: эта spec; central stack из §15 и canonical template; local AGENTS.override; план файлов §16; audit report; текущий `tests.yml`; BDD wrappers/contracts, `RoadmapInteractionsContract`, `docs/product/storm.json`; fixture lifecycle specs; `TaskSpaceTransactionTests`, `BreadcrumbEmojiUiTests`, `TestParallelLimits`, `ReactiveUiSessionHooks`.
- Reviewer: отдельный child с ролью `independent-reviewer` просмотрел дизайн, не меняя файлов. Он сообщил effective sandbox **danger-full-access**, поэтому это advisory cross-review, **не технически изолированный read-only review**. Требуемый fallback выполнен отдельно автором: проверены cross-callers/discovery, failure propagation, missing telemetry, baseline и scope. Residual risk: защита reviewer от мутаций обеспечивалась поведением, а не sandbox.
- Decision: можно предъявить spec на подтверждение; implementation claims отсутствуют.
- Review passes:
  - Scope/Evidence: исходные 10 runs и snapshot SHA отделены от текущей базы; просмотренные исходники подтверждают только перечисленные duplicate paths.
  - Contract: сверены U1–U7, AC1–AC10, Non-Goals, state matrix, required check и lifecycle owners.
  - Adversarial risk: контрпримеры «первый helper падает», «Headless build failed без TRX», «ещё один caller старого метода», «старый TS pointer активен», «нет 10% без baseline» привели к уточнениям.
  - Role-Based: применены пять ролей из таблицы; UI appearance сохраняется, CI user workflow и security проверены отдельно.
  - Fix and re-review: перечитаны §6.2.2–6.2.3, AC2/AC4, §11, §16 после правок; проверены реальные cross-callers и registry. Финальный reviewer verdict фиксируется в журнале ниже.
  - Stop decision: SPEC PASS; EXEC и remote validation не начаты.
- Depth checklist:
  - Scope drift/unrelated changes: `git status --short` содержит только новый spec; ни tests, ни workflow не изменены.
  - Acceptance criteria: каждому AC соответствует test/check/evidence; conditional Feed и remote часть не скрыты.
  - User-observable scenarios/decisions/objections: таблицы заполнены; нет user-owned design blocker.
  - Validation evidence: structural scan секций 0–20 и таблиц, whitespace check нового файла; это не runtime validation.
  - Unsupported claims: нет CI per-test p95 из отсутствующих данных, green repeats не названы fix, 10% — ориентир.
  - Regression/edge case: lifecycle, duplicate failure paths, collision test identity, zero-discovery, cancelled job и fault completeness проверены в дизайне.
  - Comments/docs/changelog: предусмотрены test analysis/coverage docs и active registry; пользовательский release/changelog не требуется на SPEC.
  - Hidden contract change: смена фильтров явно описана, check/BDD IDs/production semantics сохраняются.
  - Manual-review challenge: требовать доказательство, что «быстрее» не означает fewer assertions; карта, invocation checks и paired runs обязательны.
- No-findings justification: не применимо — review выявил и устранил конкретные недостатки.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | coverage | После dedup первый failed helper скрывал бы четыре independent emoji outcomes | Independent subcase execution/cleanup, aggregate red, negative contract test | fixed |
| MEDIUM | integration | Roadmap search имеет второго BDD caller вне первого списка файлов | Перевести caller на helper, сохранить второй живой scenario | fixed |
| MEDIUM | traceability | Активные STORM path/symbol/filter ссылались бы на снятые entry points | Scoped registry update, historical evidence сохранить | fixed |
| MEDIUM | acceptance | TRX/HTML требовались бы и от не стартовавшего host | Различить successful completion, failure и not-executed | fixed |
| LOW | performance | Обязательные 10% вводили не подтверждённую baseline квоту | Ориентир 10%, значимый targeted gain + no unexplained full regression | fixed |

- Fixed before continuing: все перечисленные findings с однозначным исправлением.
- Checks rerun: чтение затронутых контрактов, structural/table scan и whitespace проверки; test execution не применим к docs-only SPEC.
- Needs human: только стандартное подтверждение готовой spec для перехода в EXEC.
- Residual risks: writable reviewer sandbox; неизвестная emoji-причина; экспериментальный perf effect; будущий live GitHub check после отдельной delivery authorization. Ни один не скрыт как уже успешная проверка.

### Post-EXEC Review

EXEC выполняется после подтверждения. Промежуточный advisory reviewer обнаружил и затем подтвердил исправление: skipped-as-pass в repeat series, ложную completeness частичного отчёта, подмену provenance метаданными анализатора, нестабильный fingerprint, порчу completion metadata при rejected rerun, устаревший breadcrumb expected path, риск прерывания cleanup ошибкой trace, неполную resource map и неполное selection/source-boundary coverage. Все исправления имеют локальные regression checks. Последний bounded static re-review — PASS без новых findings; это не итоговый post-EXEC verdict.

Reviewer имеет `danger-full-access`; техническая read-only изоляция недоступна, соблюдён только read-only режим действий. Отдельный author adversarial fallback выполнен по коду и финальному evidence; итоговый review зафиксирован ниже. 100-process и targeted paired validation завершены. Последняя ограниченная full performance серия v5 остановлена из-за посторонней нагрузки: результат INCONCLUSIVE, новых performance-попыток нет. Три candidate correctness executions каждого проекта завершены. Факты и ограничения собраны в `docs/testing/ci-test-implementation-evidence.md`.

Full advisory pass 06:22 MSK: reviewed approved spec, весь relevant diff и новые CI scripts/C# helpers/docs, STORM registry, реальные RED/GREEN TRX и SHA256 manifest, source-boundaries 20/20 с 10 complete traces, fault indices40/36/37, nativeHeadless38/38, harness logs. Scope/Contract/Adversarial/Role-based/Depth passes выполнены; tester, developer/architect, UX и CI/security роли рассмотрены отдельно. Семь moved bodies совпали с baseline. Найденные runner-crash/blank invocation ID/unknown argv counterexamples исправлены и повторно проверены; открытых кодовых findings нет. Stop decision — **NEEDS-FIX по незавершённой валидации**, не общий PASS. Author adversarial fallback отдельно проверил CI prerequisites/exit propagation, aggregation и историю, нашёл mixed assertion+runner crash и проверил исправление; финальный evidence pass нужен после 100/paired/full. Техническая read-only изоляция reviewer не заявляется.

Уточнения EXEC в разрешённых границах: controlled emoji дал related enumeration и selection defects с собственными RED/GREEN, но не доказал исторический Avalonia index failure. Два task-card lightweight пилота отклонены как требующие выделения production XAML component; сохранены real full UI tests с phase trace. BDD wrappers получили metadata-only `CiMeasurementPackage` для сопоставимого измерения пакета за один process, поскольку документированный OR filter дал zero discovery в установленном runner. Baseline архив получил идентичную instrumentation и metadata, но не оптимизации; raw первоначальный timeout исключён из performance samples.

Catalog seed откатан: пять пар дали median +12.1% и практически одинаковый mean, без значимого выигрыша. Breadcrumb на пяти чистых парах дал median −70.8%; этот targeted результат сохранён. Во время server-замеров обнаружена конкурирующая Arm.Srv test/RavenDB нагрузка, стартовавшая после окончания breadcrumb/Catalog. Server и последующие пакеты первой серии исключены из принимаемого performance evidence; чужие процессы не изменялись. Для следующих замеров добавлены ожидание свободной машины и наблюдение за посторонними build/test процессами без сохранения их command lines. Это контроль достоверности измерений, не изменение CI scheduling или расширение продуктового scope.

Финальный bounded stop: v5 содержит 4 warmup + 7 measured processes, все с ожидаемым Passed count; candidate Main2 906/906 при native0 получил `environmentEligible=false`. Driver сохранил raw row и остановился. Полных пригодных трёх пар нет: AC9 full performance — **INCONCLUSIVE**, общий outcome остаётся qualified/PARTIAL даже при последующих green correctness runs. Main1+2 и Headless1 пригодны для correctness на неизменных SHA256; поздние Main3/Headless2+3 не считаются performance samples. Advisory reviewer проверил row/monitor/driver/hash границы и подтвердил этот stop/reuse; foreign load не объявлена единственной доказанной причиной замедления.

| AC | Локальный итог | Evidence / остаточная граница |
| --- | --- | --- |
| AC1 | PASS локально; GitHub pending | Native exit/orchestration negative harness, YAML/helper contract; live `if` после delivery |
| AC2 | PASS локально; GitHub pending | TRX/HTML/metadata и actual reporter: Main1+2 по906, Headless1 38, telemetry errors0; upload ещё не исполнялся |
| AC3 | PASS | Reporter adversarial fixtures, crash classification, frozen provenance, readback actual outputs |
| AC4 | PASS | Coverage map, 9 duplicate entry points сняты, BDD/subcases и второй Roadmap caller сохранены; discovery904→906 объяснён |
| AC5 | PASS для принятого пилота | Breadcrumb5paired median−70.8% и full UI страховка; card/theme пилоты отклонены, assertions сохранены; late Safe workaround имеет отдельные проверки и ограничения |
| AC6 | PASS | Все6 Main traces v5: одинаковые recorded writes и полные executed/passed40/36/37; seed откатан |
| AC7 | PASS для controlled enumeration/selection fixes | RED0/2→GREEN2/2,100process200/200 на preservedFDDBbinary с impact-based carry, affected16/16+BDD,3full candidate906/906; исторический#221 observed on baseline, causal fix не доказан |
| AC8 | PASS в пределах process/resource contract | Minimal probe+resource map; три distinct candidate Main trace:906lifecycle/bodyjoins каждый,complete/no overlap/openleases; Headless3×38spans/max1; не заявлять global serial execution или dependency worker drain |
| AC9 | PARTIAL: full INCONCLUSIVE | Targeted breadcrumb/server/emoji/search gains доказаны; старые бинарники явно обозначены; нет3пригодныхfullпар, mean/median/max/raw сохранены без полного perf PASS |
| AC10 | PASS локально; GitHub pending | Финальные builds green, Main3×906 иHeadless3×38=2832Passed,12hashes verified,новыхfixtureкаталоговнет; knownSafe suppressionоговорена; Feed N/A наf39. Main3поднагрузкой31m54sнеподтверждаетCI30mbudget |

#### Финальный post-EXEC review и completion gate

- Статус: **PASS для локального результата с оговорками**, final advisory evidence readback завершён 14:01 MSK. Прежний runtime gate закрыт. Полный performance остаётся PARTIAL/INCONCLUSIVE; это не PASS всех GitHub/performance критериев.
- Scope reviewed: approved spec, U1–U7, AC1–AC10, Non-Goals и decision ledger; весь scoped diff и 19 новых файлов, `.github/workflows/tests.yml`, семь CI-скриптов, два production изменения, BDD helpers/registry, diagnostic/lifecycle/fault helpers, четыре документа `docs/testing`; local evidence ниже.
- Review passes:
  - Scope/Evidence: точные native outcomes и counters сверены с TRX, body/case spans — с HTML, execution identity — с invocation/stage metadata; `validation-summary.json` проверяет шесть отдельных full runs, 12 SHA256, три complete Main traces и наборы fault cases. Старые failed cohorts и contamination сохранены.
  - Contract: U1–U3 подтверждены локальным orchestration/report harness и реальными reports, remote wiring pending; U4 — coverage map/discovery/BDD; U5 — controlled RED/GREEN/100series/affected/full при отдельном unproven#221; U6 — targeted paired и честный full INCONCLUSIVE; U7 — missing/cancel/crash negative fixtures. Все AC имеют явный статус выше.
  - Adversarial risk: отдельно автором повторно проверены failure propagation после первого BDD assertion, unsafe cleanup stop, deferred trace errors, mixed runner crash, missing/dirty provenance, CI prerequisites/always/upload allowlist, snapshot/selection semantics и границы измерений. Нет новых counterexamples, требующих code change.
  - Role-Based: domain — правила фильтров и task state сохранены; UX — keyboard/popup/selection и full real-control страховки сохранены, отсутствие screenshot/video раскрыто; tester — counts/variants/negative paths/fault cuts сверены; architect — ограниченный production scope, mutable shared seed отсутствует; operations/security — прежние check/permissions/30m, failures остаются red, raw dump/command lines не upload, Git delivery отсутствует.
  - Fix and re-review: skipped-as-pass, provenance/fingerprint, runner-crash completeness, breadcrumb expected path, trace cleanup, source producer isolation, CompletedTree readiness, stale CI-contract assertion и три raw Headless factory replacements имеют повторные targeted/full проверки. Ни один старый failed run не переименован в Passed.
  - Stop decision: можно завершать локальную задачу с оговорками; не запускать новые performance cohorts. Корректность локально PASS, full performance INCONCLUSIVE по заранее заданному stop. Final advisory readback подтвердил шесть уникальных invocation IDs, 2832 Passed, 12 актуальных hashes, Main3 trace/body/fault sets и фактический cleanup; все GitHub AC полностью закрытыми не объявляются.
- Evidence inspected: `emoji-100-v1-summary.json`, `sealed-emoji-comparison-v1`, `paired-targeted-v1`/`v2`,failed `paired-full-v1`…`v4`, `paired-full-v5/{boundary-summary,traces,bodies,comparison,phases}.json`, real `candidate-reports`, `candidate-full-correctness-v1/{validation-summary,traces,bodies}.json`, `headless-reports`/`main-report`, build/harness/negative lifecycle logs, coverage/resource maps.
- Depth checklist:
  - Scope/unrelated changes: git status/diff ограничены согласованной задачей; derived CI assertion и выявленная fixture readiness описаны в таблице файлов/журнале. Production вне двух разрешённых файлов, packages, Feed и persisted schema не менялись.
  - Acceptance/scenarios/objections: «ускорение удалением assertions» закрыто mapping/BDD; «emoji просто green» — RED/GREEN; «Feed backport» — N/A; «необоснованный процент» — INCONCLUSIVE; shared timeout и local-only artifacts остаются раскрытыми ограничениями, не обещаниями.
  - Validation/unsupported claims: 2832 Passed относятся к шести полным процессам на финальных бинарниках; серия 100 процессов — к сохранённой FDDB-сборке, targeted timings — к прежним test binaries. Main3 под 100% CPU не является performance sample; отсутствие трёх полных пар не маскируется медианой первой пары.
  - Regression/edge cases: unsafe continuation, empty/add/remove source, failed/cancelled/malformed reports, fault rollback/recovery, cleanup errors и resource overlap проверены. Known Safe catch не доказывает worker drain и не позволяет утверждать отсутствие подавленных NRE.
  - Docs/comments/changelog: новые команды, filters, coverage, resources и evidence описаны; release/changelog/version не менялись, публикации нет. Syntax parse 7/7, STORM JSON, git diff check и trailing whitespace в 19 новых файлах проверены без ошибок.
  - Hidden contracts/manual-review challenge: Main3 занял 31m54s при локальном лимите 45m, поэтому вместимость CI в 30m не доказана. Ключевой вопрос ручного review — не выданы ли incomplete performance/remote/visual evidence за успех; эти границы сохранены.
- No-findings justification: последний static advisory pass и отдельный author fallback не нашли новых defects после перечисленных fixes; concrete inputs и depth указаны выше. Это не доказательство устранения любого flaky behavior.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| — | Финальный scoped code/evidence review | Нет находок | Не требуется: последние artifacts проверены advisory reviewer | PASS с описанными ограничениями |
| — | Full performance | Неполные пары/посторонняя нагрузка | Сохранить raw и INCONCLUSIVE, не считать correctness replacement | bounded research stop |

- Fixed before final report: перечисленные однозначные code/test/report findings исправлены и проверены; неэффективный Catalog seed откатан, card/theme lightweight changes отклонены.
- Checks rerun: affected builds, negative harnesses, targeted UI/BDD/lifecycle, controlled RED/GREEN, 100 emoji processes, paired targeted, три full runs каждого проекта, actual reporter, traces/hash/fixture checks.
- Needs human: нет блокирующего выбора для локального завершения. Git delivery/remote validation требуют отдельного запроса; здесь они не запрашивались и не выполнялись.
- Residual risks: full performance INCONCLUSIVE и непроверенный CI30m budget; недоказанное причинное исправление исторического #221; upstream Safe suppression без worker drain proof; visual fallback; writable advisory sandbox. Это qualified local result, не полный performance/GitHub PASS.

## Approval

Подтверждение получено 31.08.2026: «Спеку подтверждаю». По `quest-mode` разрешён локальный EXEC в описанных границах. Последующим запросом «Оформи mr» пользователь отдельно разрешил commit/push и создание GitHub PR. Выбран Draft из-за неполного full performance evidence и ещё не проверенного live CI; merge, deploy и release не запрашивались.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Проверка базы и audit evidence | 0.98 | Причина emoji и future before/after — только EXEC | Подготовить дизайн | Нет | Пользователь попросил spec рекомендаций | Не смешивать текущую базу и Daily Feed; не повторять закрытые fixes | Audit report, workflow, BDD/helpers, lifecycle specs |
| SPEC | Первый полный черновик | 0.93 | Результат adversarial review | Review и исправления | После quality gate — approval | Нет запроса подтверждения до готового документа | Зафиксированы scope, measurement gates и evidence boundaries | Эта spec |
| SPEC | Post-SPEC review и исправления | 0.96 | Точная причина emoji/эффект perf остаются EXEC evidence | Финальные проверки документа | Нет, все review fixes однозначны | Отдельный advisory reviewer сообщил PASS после re-review; пользовательские уточнения не требовались | Закрыты subcase fail-fast, второй caller, active registry, missing reports и необоснованная квота ускорения; sandbox limitation раскрыт | Эта spec, read-only inspection исходников |
| SPEC | Предъявление готовой спецификации | 0.97 | Подтверждение перехода в EXEC | Ожидать «Спеку подтверждаю» | Да: стандартный QUEST gate | Spec передаётся пользователю; подтверждение ещё не получено | Linter готов, rubric 27/30, технический review PASS; implementation и Git delivery не выполнялись | Эта spec |
| EXEC | Approval и baseline | 1.00 | Полные baseline результаты выполняются | CI/report implementation | Нет | Пользователь: «Спеку подтверждаю» 31.08.2026 | Создана согласованная ветка, baseline source archive от f39b3245 в отдельном temp path; Git delivery не выполнялась | Эта spec, artifacts/ci-implementation-2026-08-31 |
| EXEC | CI telemetry и failure propagation | 0.96 | Live GitHub wiring ждёт отдельной delivery | BDD и targeted checks | Нет | Нет | Reporter: 9 contracts PASS; stage helper: 5 failure cases PASS. Workflow сохраняет All tests/permissions и собственные prerequisites | scripts/ci, .github/workflows/tests.yml |
| EXEC | BDD dedup и контролируемая регрессия | 0.95 | Full/paired validation и historical index failure | Проверить минимальный snapshot fix | Нет | Нет | 7 UI bodies сохранены во вложенных static helpers, 2 live duplicates сняты; 3 failure-continuation tests PASS. Controlled exclude All дал InvalidOperationException при изменении коллекции во время foreach; это related repro, не точное доказательство historical ArgumentOutOfRangeException | Test helpers, coverage map, emoji-reentrant-red |
| EXEC | Targeted checks и устранение review findings | 0.97 | 100-process и полная приёмка | Серии на финальной сборке | Нет | Нет | 16 targeted filters green; reporter/orchestration/series harness green после последних исправлений. Probe HTML подтвердил 2 concurrent independent bodies и отсутствие shared-resource overlap | targeted-v2, scripts/ci, probe-bodies.json |
| EXEC | Проверка performance гипотез | 0.99 | Чистые server/emoji/search и full samples | Откат seed; повтор после конкурирующей нагрузки | Нет | Нет | Breadcrumb retained; Catalog reverted; шумные samples не объявлены ускорением. Чужие процессы сохранены | paired-targeted-v1, environment-incident.json, evidence document |
| EXEC | Проверка настоящего runner и controlled-source isolation | 0.97 | Source-boundaries failure требует проверки после изоляции autosave | Пересборка, targeted, 100-repeat/full | Нет | Нет | Attached results-directory исправляет ранний SDK invocation failure; v4 matrices3/3 и reporter green. Новый boundary-test дал KeyNotFound при restore с активным autosave; producers изолированы только в двух новых UI probes, assertions сохранены, cleanup не повторяет successful restore и сохраняет primary error | final-targeted-v4, *-attached.log, implementation evidence |
| EXEC | Проверка исправленной изоляции и новый RED/GREEN | 0.99 | 100-process и paired full продолжаются | Дождаться серий и обновить acceptance evidence | Нет | Нет | Main build green; source-boundaries10processes20/20 с complete traces; одинаковые final tests против baseline production DLLs0/2, candidate2/2, причины selection/enumeration сохранены; nativeCIhelperHeadless38/38 и reporter0errors | source-boundaries-v5, sealed-emoji-comparison-v1, native-stage-smoke |
| EXEC | Adversarial review reporter | 0.98 | Повтор negative fixtures после последних metadata guards | Прогнать harness и real artifact readback | Нет | Нет | Runner crash с уже упавшим тестом больше не маскируется как complete assertion failure; пустые invocationId/args не дают completeness/comparability | scripts/ci, Test-TestReport-crash-classification.log, Test-TestReport-provenance.log |
| EXEC | Завершение controlled emoji validation | 0.99 | Чистые парные и полные замеры | 36 targeted processes с контролем среды | Нет | Нет | 100/100 процессов, 200/200 Passed; 100 complete traces, 200 matched bodies, 0 resource overlap. Затем affected class16/16 и BDD1/1, realreporter0errors. Historical index failure не объявлен исправленным | emoji-100-v1-summary.json, affected-emoji-class-v1, affected-emoji-bdd-v1 |
| EXEC | Завершение чистых targeted замеров | 0.99 | Full comparison, три полных traces каждого варианта | 16 full processes: warmup и три парных измерения обоих проектов | Нет | Нет | 36/36 процессов; по5 paired samples: server−43.8%, emoji−37.6%, search−38.7% median. Чужая нагрузка не наблюдалась, все traces complete. Full объём/ETA3–5ч и путь progress сообщены до запуска | paired-targeted-v2/comparison.json, phases.json, traces.json, paired-full-v1 |
| EXEC | Полный прогрев выявил readiness race CompletedTree | 0.98 | Targeted/full после узкой fixture правки | Дождаться начатых saves перед lazy UI subscription; новый парный cohort | Нет, test-only readiness сохраняет контракт | Advisory reviewer HIGH, предложенная правка допустима; общего PASS нет | Baseline904/904+38/38; candidate завис603lifecycle, сняты stacks/dump, собственный PID остановлен,0measuredsamples. Одинаковая preparation добавлена обоим вариантам; production вне emoji не менялся | paired-full-v1/hang-incident.json, MainControlTreeCommandsUiTests, revised baseline v2, implementation evidence |
| EXEC | CompletedTree readiness повторно проверена | 0.99 | Full v2 и итоговый evidence review | Новая серия16processes с равной preparation обоихвариантов | Нет | Relevant advisory static PASS;100emoji evidence переносится только для неизменной поверхности | Candidate10×4=40/40 и revised baseline10×4=40/40,20complete traces0overlaps; новый emoji16/16+BDD1/1. Production DLL SHA неизменны, старый Main test binary сохранён; prep manifest и build logs готовы | completed-fixture-readiness.json, full-v2-preparation/prepared.json, paired-full-v2 |
| EXEC | Актуализация CI/README contract после делегирования команд | 0.99 | Full v3 и итоговый evidence review | Два новых candidate warmup и все12measured; два прежних baseline warmup по проверенной ссылке | Нет | Advisory static re-review PASS; warmup reuse допустим при проверке бинарников/среды | Fullv2candidate905/906: единственная stale static YAML assertion исправлена, UI smoke сохранён; targeted1/1 и reporter0errors. Production/Headless DLL прежние. Reuse guard5contractsPASS, проверяет manifest reference strings, не их содержимое; raw failed candidate не переносится | CiReadmeMediaContract, ci-readme-contract-v1, before-ci-contract-fix, paired-full-v3/baseline-warmup-reuse.json |
| EXEC | Upstream Headless teardown failure в третьем candidate | 0.99 | Targeted и новый homogeneous full cohort | Три raw sessions на existing Safe helper в обоих вариантах, без расширения catch | Нет, narrow test-only workaround в затронутом классе | Advisory HIGH подтвердил допустимость трёх замен; общий PASS запрещён до новой валидации | v3warmup и2полныепарыgreen, thirdcandidate905/906 NREDispose. Изолированный probe3null_dispatchTask/2033sessions воспроизвёл dependencyrace; старые2пары не смешиваются с новойсборкой. Сохраняются productiondiff, assertions, Close/Clean; underlyinglifecycleнеобъявленfixed | Headless-start-race evidence, before-headless-workaround, revisedbaselinev3, full-v4-preparation |
| EXEC | Workaround точечно проверен, новый full cohort | 0.99 | Полные три пары v4 | Дождаться16процессов и завершить AC/evidence review | Нет | Relevant static re-review PASS; full pending | 4buildsgreen, layout10×2=20/20, BDD5/5,class16/16,session2/2;17complete traces0overlaps. Negative linked-helper contract сохраняет body/cleanup exceptions; workerdrainпослеsuppressionнезаявляется. MainBA703..., other5DLLunchanged | headless-workaround-*, safe-headless-contract.log, paired-full-v4 |
| EXEC | Исходный historical index failure наблюдён на baseline | 0.99 | Последний bounded perfcohort и candidatefullcorrectness | v5samebinaries4warm+3pairs; любойfailure=>INCONCLUSIVEбезv6, candidate3fullотдельно | Нет, requiredsamplesнесокращены; researchstopявный | Advisoryдопустилодинзаранееограниченныйcohort, LOWstalehistoricalclaimисправлен | v4baselinewarm903/904 native2, тотжеAllItemtest иList.Insert/PanelContainerGenerator/SortedAdaptor,0measured. Cause/fixlinkдля#221необъявленыдоказанными. Старыйfailedrunсохраняется; дажеgreenperfописываетlatencyуспешныхruns | paired-full-v4, implementationevidence, будущийpaired-full-v5 |
| EXEC | Full performance остановлено по заранее заданной границе | 0.99 | Последний Main correctness и final evidence review | Не повторять performance; завершить candidate correctness и явно оставить AC9 PARTIAL | Нет | Advisory проверил stop/reuse, новых findings нет | v5:4warm+7measured, candidateMain2 906/906/native0, timing invalid из-за48наблюдений8чужихпроцессов. Одна пригодная пара не заменяет3; raw сохранён, v6нет. Headless2+3 по38/38; Main3 выполняется на тех же12проверенных DLL | paired-full-v5/boundary-summary.json, candidate-full-correctness-v1, implementation evidence |
| EXEC | Финальная локальная приёмка и review | 0.99 | Live GitHub/30m budget и доказанный full performance остаются отдельными ограничениями | Завершить локальный результат; новых perf cohorts и Git delivery нет | Нет | Advisory final readback PASS 14:01 MSK, техническая read-only изоляция не заявлена | Main3 906/906; всего 3×906 + 3×38 = 2832 Passed, 12 hashes проверены, complete traces без overlaps/open leases, fault sets сохранены, новых fixture каталогов нет. Author fallback и relevant re-review завершены. Full performance INCONCLUSIVE, historical #221 causal fix не доказан | validation-summary.json, main-report, итоговые docs/testing и AC-таблица |
| EXEC | Оформление GitHub PR по отдельному запросу | 1.00 | Live CI после публикации ветки | Разделить commit scopes, push рабочей ветки и открыть Draft PR в main с evidence/risks/rollback | Нет | Пользователь: «Оформи mr» | Origin main по-прежнему f39b3245, расхождения нет; существующего PR для ветки нет. Main защищён review и обязательными checks. Локальный код и проверенные бинарники не меняются; raw artifacts остаются ignored | Git commits, PR description, эта spec |
