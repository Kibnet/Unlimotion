# Ускорение полного запуска тестов без сокращения проверок

## 0. Метаданные
- Форма: Expanded, medium. Меняется CI orchestration; short SPEC неприменима из-за config и межпроцессного взаимодействия.
- Владелец: Codex; решение о переходе в EXEC — Павел.
- Тип: delivery-task, performance-optimization; основной stack — .NET desktop, context — testing-dotnet.
- Stack: central AGENTS → routing-matrix → creator-vibe-lens → model-behavior-baseline → tool-execution-baseline → quest-governance/quest-mode → collaboration-baseline → testing-baseline/testing-dotnet → performance-optimization; spec-linter/spec-rubric/review-loops; локальный AGENTS.override.
- Поверхность: Codex desktop, Windows/PowerShell; .NET SDK 10.0.402, global.json 10.0.400/latestPatch. Изменение модели агента не требуется; model eval неприменим.
- Checkout: `C:\Users\Kibnet\.codex\worktrees\14fd\Unlimotion`, detached HEAD `690d4184`; перед EXEC сверить drift и создать локальную ветку `perf/tests-ci-runtime`.
- Разрешённый исходным поручением результат: реализовать ускорение тестов без потери качества покрытия. Публикация, push, PR, merge и изменение GitHub settings не входят в этот результат.
- Canonical template: `C:\Users\Kibnet\.codex\agents\templates\specs\_template.md`.

**Итог EXEC, 2026-10-20:** принята только оптимизация reporter (95,004 → 2,997 с по медиане трёх paired runs, −96,845%). Concurrent-часть отклонена по предусмотренному AC5 fallback; workflow, app и TUnit tests не изменены. Разделы 6/13 сохраняют исходно утверждённый дизайн и ход исследования; фактический результат, AC disposition и evidence приведены в post-EXEC review.

## 1. Overview / Цель
Сократить время полного обязательного запуска за счёт индексирования диагностических записей и перекрытия выполнения двух существующих тестовых проектов в отдельных процессах.

Outcome contract:
- Исходный запрос: «Проверь ... как можно их ускорить ... без потери качества покрытия», затем «Давай делай».
- Success means: все прежние случаи и проверки выполняются, failures остаются failures, а принятые оптимизации имеют сопоставимые замеры до/после.
- Output: изменения скриптов и workflow, содержательные contract tests, результаты обоих полных запусков и performance evidence.
- Stop: при неустранённой регрессии, пропавшем сценарии или отсутствии обязательного evidence результат не считается готовым. Не оставлять параллельный режим, если эффект не подтверждён.

## 2. Текущее состояние (AS-IS)
- `.github/workflows/tests.yml`: один job/check `All tests`; restore и build двух проектов, затем последовательные test/main и test/headless, общий reporter, два artifact upload.
- `Invoke-TestStage.ps1`: TUnit/MTP, `--maximum-parallel-tests 1`, `--minimum-expected-tests 1`, `--no-build --no-restore`; metadata и результаты разделены по main/headless.
- `Write-TestReport.ps1:128–138`: для каждого теста заново сканирует traceEntries для поиска начала и фаз. Обрабатывает ошибки metadata/TRX/JSONL и не выдаёт отсутствующие результаты за успешные.
- CI 38173809477, другой SHA: job 29:05, main 20:21, headless 3:33, reporter 1:45.
- CI 39268993531, другой SHA: job 32:21, main 22:44, headless 4:02, reporter 1:58. Это наблюдения разных исходников, не before/after эксперимент.
- Локальный replay исходного reporter на втором artifact: 98,425 с, оба project tests.json побайтно совпали с оригиналом; telemetryErrors=0. В диагностическом эксперименте индексированное сопоставление 1600 записей с 8804 trace rows: 0,377 с, 1525 сопоставлений, 0 расхождений. Это время части алгоритма, а не нового reporter целиком.
- Evidence baseline: `%TEMP%\unlimotion-test-audit-20261019-14fd\{39268993531,38173809477,report-input,report-replay}`. Артефакты вне Git; при их отсутствии повторно скачать существующий CI artifact.
- В normalized main report 1600 записей: 1572 записи верхнего runner и 28 дочерних записей. Не складывать времена родителя и ребёнка как независимые расходы.
- Общие mutable Avalonia/ReactiveUI globals требуют последовательности внутри каждого процесса. Отдельные сборки имеют общие project outputs и должны оставаться последовательными.

## 3. Проблема
Обязательные проверки ждут друг друга на уровне независимых процессов, а формирование отчёта выполняет многократный линейный поиск по одним диагностическим данным.

## 4. Цели дизайна
Сохранить тестовые сценарии, достоверную отчётность, единый required check, простую диагностику failures и обычный локальный путь последовательного запуска. Не менять production поведение ради скорости тестов.

## 5. Non-Goals
- Не удалять/skip-ить проверки и параметры, не уменьшать benchmark 3000×600 или визуальные матрицы.
- Не повышать параллелизм TUnit внутри процессов, не объединять Avalonia sessions, не сокращать таймауты и debounce-окна проверок.
- Не менять UI, storage, приложение, line-coverage конфигурацию или native FlaUI workflow.
- В этом этапе не выделять benchmark в третье задание: это отдельная оптимизация с собственным partition/measurement контрактом. Он продолжает обязательно выполняться в main.
- Не создавать дополнительные runner/jobs и не менять required checks в GitHub.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `Write-TestReport.ps1` — индексировать trace, сохраняя текущие форматы и трактовку данных.
- Новый `scripts/ci/Invoke-TestProjects.ps1` — запустить только подготовленные проекты в отдельных pwsh-процессах через существующий Invoke-TestStage, дождаться обоих, вернуть общий результат.
- `tests.yml` — прежние последовательные restore/build, один параллельный test orchestration step, прежние always-report/upload.
- Contract tests — подтвердить совпадение отчётности и fail-closed orchestration.

### 6.2 Детальный дизайн
**Reporter.** Один проход по traceEntries создаёт индексы начал по паре className/displayName и фаз по diagnostic context ID. Хранить все совпадения и исходный порядок; сопоставление разрешать только при прежних условиях identityStatus=complete, ровно одно начало, непустой context ID. Не заменять дубликаты последним элементом словаря. Сохранить прежнюю нечувствительность строкового сравнения к регистру; characterizations проверить на текущей PowerShell culture, включая не-ASCII. Не использовать неоднозначную конкатенацию ключей с разделителем: вложенные индексы или структурно закодированный ключ. Сохранить null/пустой массив и phasesMissingReason, existing exit/status/errors, раздельный учёт parent/child.

**Orchestration.** Принимает ResultsRoot и явный набор main/headless, выбранный workflow по успешным build outcomes. Проекты с неуспешной сборкой не запускать; отсутствие обязательного проекта не превращать в успех всего job. Набор пуст — явная ошибка. На обычном запуске оба проекта выполняются полностью без фильтров. Один coordinator запускает отдельный pwsh на каждый Invoke-TestStage; процессы имеют собственное окружение, trace directory и отдельные stdout/stderr log paths. На Windows Start-Process использовать с WindowStyle Hidden; аргументы передавать без string-built executable expressions, отдельно проверить пути с пробелами и Unicode. Существующий скрипт не dot-source-ить: его exit должен завершать только дочерний процесс. После старта обоих ждать завершения каждого, не fail-fast на первом test failure. Печатать выход каждого с названием проекта; сохранять полные логи отдельно.

Общий exit=0 допустим только при успехе всех запущенных проектов; любой ненулевой exit или ошибка запуска — общий failure с сохранением исходных per-project exit в metadata/log. Если один процесс не стартовал, дождаться уже стартовавшего здорового проекта и сохранить его evidence. В finally при прерывании coordinator завершать только принадлежащие ему живые process trees; не искать и не завершать чужие dotnet/pwsh. Timeout job остаётся 30 минут, искусственно увеличивать его ради зелёного результата запрещено. Cancellation не трактуется как PASS.

**Workflow.** Сохранить job id all-tests, имя All tests, permissions contents:read, checkout ref, cache, timeout, concurrency и оба upload name/retention/path. Test step имеет !cancelled() и условие готовности хотя бы одного проекта; передаёт ему только прошедшие build проекты. Предыдущий failed build уже оставляет job красным. Summarize по-прежнему always с фактическим job.status и default Projects main/headless, поэтому недостающий обязательный набор остаётся видимым. Обновить структурные CI assertions под orchestration; не ослаблять их до простого наличия произвольной строки.

**Изоляция.** Не запускать restore/build параллельно. В review проверить пересечение внешних путей/config/артефактов test процессов. При обнаружении общей mutable внешней среды устранить её test-only изоляцией в этом scope либо отказаться от concurrent режима, сохранив оптимизацию reporter.

Visual planning и видео: неприменимы — UI automation scenarios, selectors, app flows и визуальные эталоны не меняются. Существующие UI тесты входят в полную валидацию, evidence здесь — TRX, metadata и process timeline.

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Expected output | Evidence | AC |
| --- | --- | --- | --- | --- |
| Полный CI | Обычный push/PR workflow после будущей публикации | All tests отражает оба прежних набора, заканчивается быстрее | локальный эквивалент workflow, duration/TRX | AC1–4 |
| Failure main | main падает, headless здоров | Общий failure, headless полностью выполнен и отчёт доступен | process stubs и metadata | AC2 |
| Failure build/runner | Один проект не собран либо процесс не стартовал | Здоровый проект выполняется; нет ложного общего PASS | отрицательные contracts | AC2 |
| Большая диагностика | Те же сохранённые CI artifacts | Те же результаты/ошибки, сокращено время reporter | replay и сравнение JSON | AC1, AC3 |

### 6.4 State / Interaction Matrix
| State | Trigger | Result | Edge case |
| --- | --- | --- | --- |
| Оба проекта собраны | orchestration | Два разных процесса, подтверждённое перекрытие | отдельные stdout/trace |
| Собран один | orchestration | Выполняется только он, job остаётся failure | reporter видит отсутствие второго |
| Один test failure | Второй работает | Дождаться второго, вернуть failure | не fail-fast |
| Один start failure | Второй уже запущен | Сохранить/дождаться второго, failure | лог launch error |
| Cancellation | coordinator teardown | Остановить только собственные оставшиеся деревья | не маскировать interruption |
| Дубликат trace identity | phase join | Сохранить ambiguity, не выбирать один trace | прежние результаты |

### 6.5 Decision Ledger
| Decision | Owner | Chosen option | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Scope первого ускорения | agent | Reporter + concurrent main/headless | 0.95 | CPU/IO contention | Нет |
| Сохранение All tests | agent | Один прежний job, отдельные процессы | 0.95 | Ошибка coordinator | Нет |
| Benchmark | agent | Выполнять без изменений внутри main | 0.95 | Host contention влияет на его timings; GC остаётся process-local | Нет |
| Остальные оптимизации | agent | Не входят в этот change set | 0.95 | Резерв ускорения останется | Нет |
| Переход SPEC→EXEC | user | Получено 2026-10-20 | 1.0 | Exact approval зафиксировано в журнале | Нет, approval получено |

### 6.6 Runtime / Config / Data Contract Matrix
| Area | Source of truth | Change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Required check | tests.yml / All tests | Test orchestration step | Имя/job/gate сохраняются | Workflow contracts |
| Invocation | Invoke-TestStage / metadata | Вызов в дочернем pwsh | Схема/arguments/exit прежние | Native stubs + full run |
| Test inventory | Полный baseline TRX/discovery этого checkout | Раздельное одновременное выполнение | Без фильтрации/skip | Мультимножества identity и outcome по project/parent-child |
| Reports | Write-TestReport | Indexed correlation | Форматы и failure semantics прежние | fixtures + artifact replay |
| Data приложения | Production sources/settings | Нет изменений | Миграция не нужна | Diff scope |

## 7. Бизнес-правила / Алгоритмы
1. All tests зелёный только после обязательных restore/build/test и успешной проверки полноты отчёта.
2. TUnit maximum parallel tests=1 в каждом процессе; benchmark остаётся в исходном полном наборе.
3. Duplicate identities/ambiguous starts не объединять и не разрешать догадкой.
4. Для оценки test inventory родительские и дочерние результаты сравнивать отдельно, не смешивать агрегированные durations с wall-clock.
5. Ускорение не является основанием ослабить assertions, таймауты, ошибки telemetry или обязательность другого проекта.

## 8. Точки интеграции и триггеры
После обеих build стадий coordinator вызывает Invoke-TestStage test для доступных проектов. Reporter запускается после завершения coordinator через always. Test-TestOrchestration и CiReadmeMediaContract защищают новый entry point и сохранение исходного gate.

## 9. Изменения модели данных / состояния
Только временные индексы в памяти и отдельные log files coordinator. Новых persisted полей приложения/миграций/публичных API нет. Invocation/stage/TRX/normalized report schemas сохраняются.

## 10. Миграция / Rollout / Rollback
Локальная реализация после approval; сначала tests/replay, затем локальный полный эквивалент workflow. GitHub активация не входит в поручение: push/PR не выполнять. Rollback независим по двум оптимизациям: вернуть последовательные test steps и убрать coordinator; отдельно вернуть прежний phase join. Не трогать пользовательские данные, результаты предыдущих запусков или unrelated файлы.

## 11. Тестирование и критерии приёмки
- AC1: normalized project tests.json на сохранённом artifact побайтно равны baseline; meaningful run fields/status/errors/summary совпадают, кроме generatedUtc и путей вывода. Fixtures сохраняют ошибки malformed metadata/TRX/JSONL, zero discovery, partial cancellation, duplicates и ambiguity.
- AC2: orchestration подтверждает overlap двух stub процессов, последовательность build→test, ожидание здорового проекта после failure соседа, launch failure/partial build/empty projects/cancellation cleanup и корректность путей с пробелами/Unicode; failures никогда не становятся общим success.
- AC3: median полного reporter replay на одном dataset/host минимум на 80% меньше baseline median; минимум три замера каждого reporter, последовательно, одинаковая конфигурация. Замер 0,377 с части алгоритма не закрывает этот AC.
- AC4: свежий serial baseline и candidate полного запуска обоих проектов на одном checkout/build/config; мультимножества стабильных тестовых identities/параметров по проектам и parent/child совпадают, top-level failures/новых skips нет; UI и benchmark присутствуют. При сравнении execution GUID и timestamp не считать identity.
- AC5: concurrent режим оставляется только при измеримом улучшении test wall-clock не меньше 10% против serial baseline, без новых failures/изоляционных проблем. При меньшем/шумном эффекте сделать повторную сопоставимую пару; если подтверждения нет, откатить только concurrency и явно завершить reporter-only outcome с объяснением, не заявляя ускорение двух наборов. Это заранее согласованная performance stop rule.
- AC6: обычные builds обоих проектов, parser checks PowerShell/YAML, существующие CI/report/targeted-series contracts, затронутые TUnit workflow contracts и git diff --check проходят.

Команды после approval (аргументы нового coordinator конкретизируются в рамках этого контракта):
```powershell
pwsh -NoProfile -File scripts/ci/Test-TestReport.ps1
pwsh -NoProfile -File scripts/ci/Test-TestOrchestration.ps1
pwsh -NoProfile -File scripts/ci/Test-TargetedTestSeries.ps1
dotnet restore src/Unlimotion.Test/Unlimotion.Test.csproj
dotnet restore tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj
dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false
dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-restore -p:UseSharedCompilation=false
pwsh -NoProfile -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project main -ResultsRoot <fresh-serial-results>
pwsh -NoProfile -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project headless -ResultsRoot <fresh-serial-results>
pwsh -NoProfile -File scripts/ci/Invoke-TestProjects.ps1 -ResultsRoot <fresh-concurrent-results>
git diff --check
```
Перед длинными запусками сообщить log path и ожидаемые 25–35 минут на пару проектов по CI baseline; локальная скорость может отличаться. Не запускать независимые benchmark измерения параллельно с baseline/candidate. При timeout не повторять идентичную команду без причины и новой гипотезы. После обязательных зелёных проверок не расширять набор без нового риска.

### Acceptance-to-Test Matrix
| AC | Automated check | Evidence | Ограничение |
| --- | --- | --- | --- |
| AC1 | Test-TestReport + synthetic duplicate/culture fixtures + artifact replay comparison | JSON/summary comparison и exit codes | До EXEC план |
| AC2 | Process stubs в Test-TestOrchestration | Process IDs/timestamps/native exits/logs | Cancellation ownership проверять своим process tree |
| AC3 | Три sequential replay baseline/candidate | Median/individual wall seconds | Не сравнивать разные SHA/datasets |
| AC4 | Serial и concurrent полные TUnit запуски | TRX, inventory diff, stage metadata | Исторический CI не заменяет свежий baseline |
| AC5 | Stage wall-clock pair и повтор при шуме | Duration table, keep/revert decision | Нет обещания runner-wide ускорения до измерения |
| AC6 | Builds, parser/contracts, affected TUnit, diff check | Logs/exit codes | Нет утверждения нового CI green без GitHub запуска |

## 12. Риски и edge cases
CPU/IO contention; общие внешние paths; PowerShell argument quoting и exit propagation; потеря здорового отчёта после failure соседа; изменение case/culture equality при индексировании; сохранение null/empty array semantics; orphan process при cancellation; duplicate parent/child records. Меры и проверки определены в 6.2 и AC1–6.

### Expected User Review Objections
| Objection | Why | Mitigation | Status |
| --- | --- | --- | --- |
| Ускорили за счёт покрытия | Главный явный запрет | Полный inventory comparison, те же UI/benchmark и assertions | mitigated |
| CI зелёный, хотя один набор не выполнился | Process orchestration failure | Негативные stubs + обязательность обоих наборов reporter | mitigated |
| Обещанный выигрыш получен из несопоставимых цифр | Исторические SHA разные | Свежий serial/candidate + replay на одном dataset | mitigated |
| Зачем оставлен дорогой benchmark | Его удаление изменило бы проверяемый workload | Benchmark остаётся обязательным, partition отложен | mitigated |

### Rework Prevention Checklist
User-visible outcome, сценарии и evidence заданы; агентские решения зафиксированы; каждый AC проверяем; границы и performance rollback заданы; review по ролям ниже. UI/screenshots неприменимы из-за отсутствия UI изменений.

## 13. План выполнения
1. После exact approval сверить checkout/drift, создать локальную ветку; сохранить исходный reporter вне рабочих source paths и свежий baseline test inventory/результаты.
2. Расширить reporter characterizations, реализовать индексы, проверить parity и три paired replay замера.
3. Добавить coordinator и process failure/overlap contracts; изменить workflow и affected CI assertions.
4. Собрать проекты последовательно, выполнить targeted/contracts и полный serial/concurrent comparison; при необходимости повторить шумный timing.
5. По AC5 оставить concurrency либо откатить её целиком; выполнить затронутые повторные проверки и post-EXEC review. Сохранить конкретное evidence и ограничения в этой SPEC.

## 14. Открытые вопросы
Существенных design решений пользователю не передаётся. Ожидается только установленный центральными инструкциями переход SPEC→EXEC. Проверка shared paths и величины ускорения — задачи EXEC с определённым stop/rollback, не основание обещать результат заранее.

## 15. Соответствие профилю
Performance-optimization: baseline до изменений, сопоставимые post-замеры, принимаются только значимые улучшения. Testing-dotnet: принятый TUnit/MTP runner, relevant contracts/build и полные оба набора. Local UI override: UI поведение не меняется; существующие UI tests остаются обязательными и выполняются в full validation. UI automation skill не требуется для изменения PowerShell orchestration.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| scripts/ci/Write-TestReport.ps1 | Индексы diagnostics join | Устранить повторные сканирования |
| scripts/ci/Test-TestReport.ps1 | Characterizations индексов и failure parity | Сохранить качество отчётности |
| scripts/ci/Invoke-TestProjects.ps1 | Кандидат испытан на stubs, затем удалён из checkout | AC5 fallback; архив в TEMP |
| scripts/ci/Test-TestOrchestration.ps1 | Исследовательские additions откачены, файл равен HEAD | CI orchestration остаётся прежней |
| .github/workflows/tests.yml | Не изменён | AC5 fallback |
| src/Unlimotion.Test/CiReadmeMediaContract.cs | Изменения откачены, файл равен HEAD | App/test assemblies остаются исходными |
| specs/2026-10-19-test-runtime-optimization.md | Решения/evidence/reviews | Локальная working SPEC |

## 17. Было → стало
| Area | Было | Стало |
| --- | --- | --- |
| Phase correlation | Повторный scan для каждого теста | Однократная индексация, прежние результаты |
| Build | Последовательно | Последовательно |
| Test/main и test/headless | Последовательно | Последовательно: concurrent-кандидат отклонён по AC5 |
| Gate/artifacts | All tests и два набора | Те же обязательства и имена |
| Coverage/workload | Полные наборы, benchmark, UI | Те же без сокращений |

## 18. Альтернативы и компромиссы
- Только reporter: меньший риск; заранее выбранный fallback при отсутствии эффекта concurrency.
- Отдельные GitHub jobs: лучше разделяют CPU, но добавляют runner quota/queue, повторные builds и контракт объединения provenance разных runner. Не выбраны для первого этапа.
- Global parallel-tests >1: требует отдельного аудита mutable globals; не входит в безопасный scope.
- Удаление/уменьшение benchmark: противоречит исходному требованию; не применяется.

## 19. Quality gate и review
### SPEC Linter Result
| Пункт | Статус | Evidence |
| --- | --- | --- |
| 1 Цель/outcome | PASS | 1, 6.3 |
| 2 AS-IS | PASS | 2, прочитанные workflow/scripts/contracts, CI artifacts |
| 3 Проблема | PASS | 3 |
| 4 Цели дизайна | PASS | 4 |
| 5 Границы | PASS | 5 |
| 6 Ответственности | PASS | 6.1 |
| 7 Интеграции | PASS | 8 |
| 8 Инварианты | PASS | 6.2, 7 |
| 9 Ошибки/recovery | PASS | 6.2, 6.4, AC2 |
| 10 Performance | PASS | AC3/5, 18 |
| 11 Данные | PASS | 9 |
| 12 Совместимость | PASS | 6.6, 10 |
| 13 Rollback | PASS | 10, AC5 |
| 14 AC | PASS | AC1–6 |
| 15 AC→evidence | PASS | 11, отрицательные fixtures |
| 16 Команды/stop | PASS | 1, 11 |
| 17 План | PASS | 13 |
| 18 Решения | PASS | 6.5, 14 |
| 19 Форма/масштаб | PASS | 0 |
| 20 Профиль | PASS | 15 |
Итог: ГОТОВО к запросу фазового approval; не означает реализацию/green тестов.

### SPEC Rubric Result
| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель/границы | 5 | Полный run без сокращений, Non-Goals |
| AS-IS | 5 | Код, реальные CI artifacts, replay |
| Конкретность | 5 | Индексы, coordinator, prerequisites/failure contract |
| Безопасность | 5 | Без production data; независимый rollback |
| Проверяемость | 5 | AC1–6, отрицательные process fixtures, full run parity |
| Автономность | 5 | Решения выбраны, stop/rollback заданы |
Итог: 30/30; readiness только SPEC, exact approval остаётся обязательным.

### Role-Based Review Result
| Роль | Применимость | Verdict | Основание |
| --- | --- | --- | --- |
| Domain/workflow | applicable | PASS | Оба обязательных проекта и честный единый gate сохраняются |
| UX/designer | Не применимо | Не применимо | UI и generated visual output не меняются |
| Tester | applicable | PASS | Inventory parity, malformed/duplicate/cancellation и process overlap |
| Architect | applicable | PASS | Изоляция процессов и последовательные builds, fallback при contention |
| Delivery/operations/security | applicable | PASS | Permissions/artifacts/check прежние, owned process cleanup, без публикации |
Это self-review по ролям, не независимое review. Medium scope; high-risk security/deploy/data migration отсутствуют. Технически read-only reviewer sandbox в текущей поверхности не установлен; выполнен отдельный adversarial self-pass, независимость не заявляется.

### Post-SPEC Review
- Scope/Evidence pass: прочитаны canonical template, quest-mode/governance, linter/rubric/review-loops, testing-baseline, testing-dotnet; текущие tests.yml, Invoke-TestStage, Write-TestReport, Test-TestOrchestration, TestInvocationMetadata, CiReadmeMediaContract. Учтены исследованные CI artifacts/replay из предыдущего этапа. Git tracked/untracked clean до создания этой SPEC, detached HEAD учтён.
- Contract pass: исходный запрос сохранён; отсутствуют изменения приложения/coverage workload и публикация; в 6.3/6.5/11/12 заполнены сценарии, решения, AC и возражения.
- Adversarial pass: проверены failure соседа, частичная build готовность, start failure, cancellation/orphans, ambiguity, culture-sensitive comparison, parent/child двойной учёт, конфликт output каталогов и неподтверждённые обещания ускорения.
- Findings: MEDIUM — separate jobs усложнили бы provenance/runner quotas; fixed в дизайне выбором отдельных процессов внутри прежнего job. MEDIUM — index могла бы скрыть duplicate starts или изменить case semantics; fixed требованиями 6.2/AC1. MEDIUM — исторические разные SHA могли бы выглядеть before/after; fixed явным ограничением baseline и AC3–5. MEDIUM — blanket параллельный build повредил бы общим outputs; fixed последовательностью builds. LOW — costly benchmark остаётся; follow-up вне scope, workload не ослаблен.
- Fix and re-review: сверены изменённые решения с существующими workflow guards и invocation metadata; failure/always-report, type/culture semantics и baseline parity внесены в AC. Полнота обязательных матриц повторно проверена.
- Depth checklist: scope drift/unrelated — отсутствуют; AC/scenarios/evidence — таблицы заполнены; unsupported claims — исторические и экспериментальные цифры разделены; regression/edge cases — выше; docs/comments — исправить устаревшие CI assertions, README media flow неизменён; hidden contract — внешние check/artifacts/security сохраняются.
- Manual-review challenge: «Не замаскирована ли потеря второго проекта и не выданы ли 0,377 с части join за полный reporter?» — оба риска имеют явные AC и отрицательные проверки.
- Residual risk: host contention может исключить concurrency из результата; AC5 предусматривает измеряемое решение и полный rollback этой части.
- Stop decision: PASS для SPEC; можно запросить exact approval. Реализация и её обязательные проверки ещё не выполнены.

### Post-EXEC Review
**Stop decision: PASS для фактически принятого reporter-only результата по заранее согласованному fallback.** Concurrent-реализация не принята и не объявляется прошедшей full validation.

#### Scope / Evidence pass
- Финальный code diff: только `scripts/ci/Write-TestReport.ps1` и `scripts/ci/Test-TestReport.ps1`; отдельно эта SPEC. Проверены оба полных diff, parser и `git diff --check`. Workflow, Invoke-TestStage, orchestration contract, C# tests и production sources равны исходному HEAD.
- Использованы сохранённый исходный reporter, полный CI dataset 39268993531 (1600 main + 66 Headless records), свежие локальные full-suite TRX/metadata, characterizations исходного и нового reporter и три paired performance runs.
- Все raw evidence ниже относительно `%TEMP%\unlimotion-test-exec-20261020-14fd`.

#### Измерения reporter
| Pair | Исходный, с | Новый, с |
| --- | ---: | ---: |
| 1 | 105,0167402 | 3,0537966 |
| 2 | 95,0043253 | 2,9970193 |
| 3 | 93,0747450 | 2,9019865 |
| Median | 95,0043253 | 2,9970193 |

- Снижение median wall-clock: **96,84538647%**, примерно 31,7 раза. Это время всего reporter на одном archived CI dataset/host, не измерение нового GitHub workflow и не ускорение выполнения самих TUnit tests.
- Во всех парах `main/tests.json`, `headless/tests.json`, combined `tests.json` и `summary.md` совпали **побайтно**. `run.json` совпал по JSON values/array order после исключения `generatedUtc`; object key order не является контрактом JSON и уже меняется между процессами исходного reporter из-за Hashtable `stages`.
- Evidence: `report-performance/timings.json`, `report-performance/result.json`, `report-performance/{baseline,candidate}-{1,2,3}/`, `Measure-Reporter.ps1`.
- Allocations/peak memory не измерялись, утверждений об экономии памяти нет. Индексы устраняют повторные полные scans; сохранены duplicates и исходный phase order.

#### Полные наборы / граница исходной нестабильности
| Run | Outcome | Evidence |
| --- | --- | --- |
| Main, полный | **1510/1510**, failed=0, skipped=0; stage wall 2750,876 с | `serial/main/stage-test.json`, parent TRX, `serial-main.log` |
| Headless, первый полный baseline | **65/66**, failed=1, skipped=0; stage wall 254,099 с | `serial/headless/`, `serial-headless.log` |
| Упавший Git-indicator case отдельно | **1/1**, failed=0, skipped=0 | `baseline-headless-targeted/`, `baseline-headless-targeted.log` |
| Headless, повторный полный | **66/66**, failed=0, skipped=0; stage wall 263,857 с | `baseline-confirmation/headless/`, `baseline-headless-confirmation.log` |

Первая ошибка — `GitIndicator_SaveSyncFailureSwitchKeyboardAndRenderedMatrix`, timeout ожидания `Idle` после retry/commit. Она возникла до любого concurrent запуска, который в итоге не начинался. Первоначальный failed TRX не заменён успешным; product/test assertions и таймауты не менялись. Причина исходной нестабильности не исправлена в этой задаче и не объявляется установленной по одному успешному повтору. Существующие UI tests, benchmark и его 3000×600 workload выполнены без сокращений; новое измерение line/branch coverage не проводилось.

#### Contract / acceptance pass
| AC | Disposition / evidence |
| --- | --- |
| AC1 | PASS: baseline/candidate reporter characterizations, три exact dataset replays; failure classifications/metadata/status сохранены |
| AC2 | Не входит в финальный результат: process candidate испытан на stubs, затем откачен; исходный `Test-TestOrchestration.ps1` после rollback снова PASS |
| AC3 | PASS: −96,845% median, все шесть длительностей сохранены, одинаковые input/host/config, последовательные runs |
| AC4 | Concurrent full-run не выполнен и не заявляется PASS; concurrent code отсутствует. Для reporter-only full TUnit code/run contracts не меняются; результаты полных исходных наборов и baseline failure boundary выше |
| AC5 | Заранее разрешённый fallback: main 2750,876 с + Headless 254,099 с дают верхний предел overlap **8,4559%**, ниже 10%. Сначала concurrent run блокировал failed baseline, затем кандидат отклонён до дорогостоящего full-run по fixed-workload upper bound. Разброс main от кэшей/нагрузки не принимается за эффект overlap |
| AC6 | PASS: оба обычных Debug builds без ошибок; reporter contracts обеих версий, исходные orchestration contracts и targeted-series contracts PASS; PowerShell parser и diff checks PASS. C# files в финальном diff отсутствуют, форматирование C# неприменимо |

Contract logs: `report-contract-baseline-final.log`, `report-contract-candidate-final.log`, `orchestration-original-final.log`, `targeted-series-contract.log`, `build-main.log`, `build-headless.log`.

#### Adversarial / fix and re-review pass
| Severity | Finding | Disposition |
| --- | --- | --- |
| MEDIUM | Ordinal/current-culture comparer мог изменить прежнее PowerShell `-eq` | Fixed: invariant comparer, equivalence checks en-US/ru-RU/tr-TR и Unicode fixture; обе версии проходят |
| MEDIUM | Dictionary overwrite мог скрыть duplicate lifecycle starts, разделитель — смешать class/display keys | Fixed: nested indexes и списки всех starts; duplicate и structured-key fixtures |
| MEDIUM | Empty phases/null, порядок фаз и контекст разных test executions могли измениться | Fixed: characterizations плюс exact replay всех records в трёх парах |
| MEDIUM | Пустой успешный run или zero discovery мог выглядеть здоровым | Проверены отдельные negative fixtures на обеих версиях, никакого ослабления reporter/runner exit semantics |
| MEDIUM | Первоначальный benchmark validator считал порядок JSON object keys частью контракта | Fixed в TEMP validator: semantic comparison `run.json`, сохраняющий array order/values и исключающий только generatedUtc; подтверждено, что изменялся лишь порядок Hashtable stages. Production code ради validator не менялся |
| MEDIUM | Дополнительная concurrency не достигала утверждённого порога даже по верхней границе | Candidate полностью откачен; сохранён архив `concurrency-candidate.patch` / `Invoke-TestProjects.candidate.ps1` и расчёт `concurrency-decision.json` |
| MEDIUM | Baseline UI failure мог быть скрыт успешным retry | Первоначальный failure, isolated retry и полный повтор хранятся раздельно; не заявлено исправление flaky case |

После фиксов повторно проверены relevant contracts, semantic metadata parity и три полных replay pairs. Откаченные files сверены с HEAD; после rollback оригинальные orchestration contracts PASS. Scope не расширялся исправлением UI поведения.

#### Role-Based / completion pass
- Domain/workflow: PASS — исходные обязательные наборы и CI gate сохранены, результат ограничен реально измеренным reporter ускорением.
- Tester: PASS — characterizations обеих версий, positive/negative cases, exact dataset parity и full-suite evidence с явно отделённым исходным failure.
- Architect: PASS — один scan создаёт индексы, нет новой runtime dependency, shared mutable UI state и app API не меняются; unused coordinator отсутствует.
- Delivery/security: PASS — workflow/permissions/check names/artifact contract прежние; stage/manifest/JSONL/XML error checks сохранены; публикация и GitHub settings не менялись.
- UX: неприменимо — UI и generated visual output не менялись. Видео/новые screenshots не требуются для этого финального diff.
- Review выполнен агентом; независимый read-only reviewer не заявляется. После rollback нет high-risk config/deploy/data изменения.

Depth checklist: исходный результат/границы сверены; unrelated changes отсутствуют; обязательные evidence actual scope получены; unsupported full-CI/concurrency/coverage claims исключены; comments и comparator semantics сверены; README/changelog/release неприменимы для локального internal CI-report optimization; manual-review challenge закрыт exact parity, negative cases и сохранением failed baseline. Остаток: существующая нестабильность Git-indicator case и неизмеренный потенциальный выигрыш иной CI топологии — follow-up вне принятого reporter-only результата, не выполненные оптимизации не заявляются готовыми.

## Approval
2026-10-20 получена точная фраза пользователя «Спеку подтверждаю». Переход в EXEC разрешён в зафиксированном scope; push/PR/merge не разрешены этим approval.

## 20. Журнал действий агента
| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| Анализ, 2026-10-19 | Найдены reporter scan, последовательные наборы, дорогой benchmark | Код + два CI artifacts; reporter replay 98,425 с и index-only 0,377 с | Подготовить реализацию | «Давай делай» | CI artifacts в TEMP |
| SPEC, 2026-10-19 | Первый этап — reporter и concurrent процессы внутри All tests; benchmark без изменений | Expanded SPEC, post-SPEC self-review PASS; код/CI не менялись | Exact approval, затем EXEC | Фраза «Спеку подтверждаю» ещё не получена | Эта SPEC |
| EXEC, 2026-10-20 | Exact approval получено, drift отсутствует, создана perf/tests-ci-runtime | Baseline reporter сохранён вне checkout; начаты последовательные restore/build/full tests | Индексы reporter и process coordinator | «Спеку подтверждаю» | scripts/ci, workflow, CI contract; evidence в TEMP/unlimotion-test-exec-20261020-14fd |
| Performance stop, 2026-10-20 | Concurrency отклонена: теоретический предел overlap около 8,4% < AC5 10%; остаётся reporter-only | Main 1510/1510; исходный Headless 65/66, отдельный failed case повторно 1/1; полный Headless confirmation и paired reporter benchmark выполняются | Получить финальное evidence reporter и проверить baseline failure boundary | Новый approval не требуется: заранее предусмотренный fallback | concurrency-decision.json и candidate patch в TEMP; tracked diff только reporter/tests |
| EXEC завершён, 2026-10-20 | Reporter-only принят: median 95,004 → 2,997 с, −96,845%; три пары exact parity; post-EXEC PASS | Main 1510/1510, полный Headless повтор 66/66; исходный failure сохранён; все relevant contracts/parser/diff checks PASS | Локальный Conventional Commit; push/PR не входят в scope | Действует approval от 2026-10-20, выполнен предусмотренный fallback | Два PS scripts и эта SPEC; raw evidence в TEMP |
