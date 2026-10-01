# CLI: старые ошибки дат не блокируют независимые изменения

Expanded SPEC: изменяется публичное поведение `apply` и общий application/storage boundary; short не подходит. Фаза SPEC, до exact approval меняется только этот файл.

## 0. Метаданные

- Тип: delivery-task; профиль .NET desktop/shared domain, context testing-dotnet; UI behavior не меняется.
- Владелец: пользователь; исполнитель Codex. Масштаб medium, public behavior/storage safety.
- Central stack: routing, creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration, QUEST, testing-baseline/testing-dotnet, spec-linter/rubric/review-loops; consumer AGENTS.override.md. Canonical expanded template прочитан из центрального каталога.
- Поверхность Codex desktop/PowerShell. Model/effort основного runtime не раскрыты; модель не участвует в runtime исправлении. Model eval не применим: C# regression/integration tests, не instruction change.
- База checkout/remote main `116f2a4f8d7387e3b8c6b6d08f34a80a4b22fde0`, latest package `1.32.0`. Текущая ветка main; implementation после approval в локальной `fix/cli-legacy-date-validation`, без push/PR/release.
- Установленный CLI `C:\Users\Kibnet\.dotnet\tools\unlimotion-cli.exe`, version JSON/package/global tool list подтверждают 1.32.0. Установка не меняется в этой задаче.

## 1. Цель / outcome

Исходный симптом: агенты в соседних чатах не могли архивировать карточки, переносить новые задачи и дополнять описания. `apply --dry-run` отказывал с `validationFailed: Planned end date cannot be earlier than planned begin date`, хотя запросы не меняли даты. Пользователь поручил исправить CLI.

Success means: наличие старой карточки с end < begin не блокирует независимый `apply` или изменение её недатовых полей. Новая/изменённая некорректная пара дат по-прежнему отклоняется атомарно, error указывает конкретную карточку. Старые даты сохраняются без автоматического исправления.

Output: локальный код, regression tests, документация и validation evidence. Не объявлять активный установленный CLI исправленным без отдельной установки/проверки.

Stop rules: не менять реальные задачи, не публиковать пакет/PR и не подменять исправление кодовой ошибки массовым исправлением пользовательских дат. Environment failures отделять от product regression. До exact approval — только SPEC.

## 2. AS-IS / evidence

- `TaskApplicationCommandService.ExecuteAsync` читает graph под существующим lock, клонирует все задачи в original/staged, применяет операции и нормализует availability. Затем `staged.Values.Any(end < begin)` отклоняет запрос независимо от его scope.
- `TaskApplicationJson.ParseDate` и domain `TryParseDate` успешно разбирают корректный offset timestamp; причина не в RFC3339 parsing.
- Проверены соседние чаты «Сделай ночной прогон» и вечернего разбора: один общий и два раздельных archive preview, перенос 61 карточки, два description-update preview дали одинаковую ошибку. `didMutate=false`. Позже отдельные узкие команды выполнялись; это не доказательство исправного `apply`.
- В активном пространстве read-only диагностика обнаружила 3 исторических инверсии среди 2980 задач; подробное CLI чтение подтвердило даты. Личные названия/ID в публичную SPEC не переносятся; данные не менялись.
- Минимальное воспроизведение установленного 1.32.0: одинаковый `createTask` request без дат, applicationId `synthetic-date-check`, выбранный ID `new-task`. В temp clean-space preview success=true/exit0; в temp-space с посторонним `legacy` begin `2026-10-02T10:00:00+03:00`, end `2026-10-01T10:00:00+03:00` — success=false/validationFailed/exit1. Оба didMutate=false. `validate` второго пространства возвращает isValid=true: команда проверяет граф/загрузку/availability, не scheduling consistency.
- Diagnostic fixtures: local-only `%TEMP%/unlimotion-date-diagnostic-20261001/{clean,inverted}`; не production dataset.
- Несвязанный untracked `specs/2026-09-28-task-importance-value-visibility.md` сохранить вне diff.

## 3. Корневая проблема

Глобальное scheduling правило используется как gate транзакции, не различая новые ошибки и сохранённые старые данные. Один исторический дефект делает все declarative applications невозможными; сообщение не содержит taskId.

## 4. Цели дизайна

- Минимальный compatibility fix в существующем application service; без обхода locks/ETags/graph validation.
- Проверять итоговую пару дат относительно original snapshot; не промежуточные состояния операций.
- Сохранить safety: новые и изменённые даты должны образовывать допустимый интервал.
- Детерминированная диагностика через существующие error.taskId/authoritativeTasks.

## 5. Non-Goals

- Не исправлять/очищать исторические даты, не мигрировать файлы, не изменять live task-space.
- Не менять request schema, parser formats, task selection, обычные create/set-status, receipt/idempotency/inspect семантику, ETag/lock/recovery или UI.
- Не расширять `validate` до нового report contract: уточнить документацию, что isValid относится к загрузке, duplicate IDs, графовым ссылкам и availability, но не к согласованности planned dates.
- Не делать commit/push/PR/merge/release, не обновлять global tool/personal skill. Это отдельная delivery/rollout просьба.

## 6. TO-BE

### 6.1 Ответственность / файлы

| Файл | Изменение |
| --- | --- |
| src/Unlimotion.TaskTreeManager/TaskApplicationCommandService.cs | replacement date gate, original-vs-staged check, actionable error |
| src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs | permanent isolated CLI regressions: preview/apply/read-back/rejection |
| src/Unlimotion.Cli/README.md | правила legacy scheduling compatibility и граница validate |
| Эта SPEC | approval, journal и actual validation/review evidence |

### 6.2 Алгоритм / контракт

В прежней точке после всех операций/NormalizeAvailability и до persistence:

1. Из staged выбрать карточки, где обе даты заданы и end < begin; детерминированно упорядочить по ordinal ID.
2. Отклонять карточку, если её не было в original **или** итоговое значение хотя бы одной из двух planned dates отличается от original. Сравнение nullable DateTimeOffset включает instant и offset (`EqualsExact` либо эквивалент); null равен только null.

Точное сохранение старой пары относится к effective original/staged снимкам после чтения хранилища, не к исходному текстовому offset файла. Existing FileTaskStorage нормализует offset в зону хоста; этот storage contract не меняется. Fixture baseline для AC1 повторно читается после SaveTasks; AC3 сравнивает persisted instants, а изменение только offset в AC2 выбирается относительно загруженного baseline.
3. Историческая некорректная пара, полностью сохранённая, не является ошибкой текущего запроса: разрешить title/description/status/relations/criteria и производные availability изменения. Не требовать, чтобы карточка была вне request scope.
4. Для впервые найденного нарушения вернуть существующий `ValidationFailed`, error.taskId, сообщение с ID и обеими датами в roundtrip ISO; authoritativeTasks — original state этой карточки, если существовала. Для нового ID original state отсутствует. operationResults остаются preview outcomes, не подтверждением записи.
5. Оставить финальную graph validation, transaction, receipt и read-back неизменными. Весь пакет при отказе не пишет никаких application changes.

Date repair разрешён: привести пару к end >= begin либо очистить одну/обе даты. Две операции над разными полями begin/end в одном пакете проверяются по финальной паре; временная инверсия допустима, если финал valid. Изменить существующую инверсию на другую инверсию нельзя. Существующий duplicate-target gate сохраняется: повторные setField/clearField для одного taskId+field в одном пакете отклоняются как ConflictingOperations. No-op установка исходного значения одного поля оставляет exact pair прежней и сохраняет legacy exemption.

UI visual planning/video: не применимо, UI/flow/layout не меняются. Full existing headless suite используется как regression, новый UI scenario не нужен. Performance: та же O(N) проверка; сортировать только кандидаты нарушений, не добавлять I/O. Data schema не меняется.

### 6.3 User-Observable Scenarios

| Сценарий | Видимый результат | Evidence | AC |
| --- | --- | --- | --- |
| Старый invalid interval + новый task/description/archive | preview и apply проходят; read-back отражает только порученные поля и допустимые derived metadata; старые даты прежние | CLI integration fixture before/after | AC1 |
| Новая или изменённая inverted pair | ValidationFailed + taskId, no mutation | negative integration + snapshots | AC2 |
| Исправление/очистка дат и атомарный двухоперационный перенос | корректный итог проходит | boundary tests | AC3 |
| Повтор/inspect после успешного apply | существующий receipt/recovery contract не регрессирует | affected/full suite | AC4 |

### 6.4 State matrix

| Original | Final pair | Result |
| --- | --- | --- |
| valid/missing/new task | inverted | reject |
| inverted | same exact dates | allow unrelated changes |
| inverted | different inverted dates | reject |
| inverted/valid | valid/equal endpoints/one null | allow |
| valid | intermediate invalid, final valid | allow |

### 6.5 Decision Ledger

| Решение | Владелец | Выбор | Confidence | Риск | Нужно до EXEC |
| --- | --- | --- | ---: | --- | --- |
| Compatibility вместо автолечения данных | agent | grandfather только неизменённой пары | 0.99 | legacy ошибки останутся | Нет: сохраняем данные и user scope |
| Error contract | agent | существующие kind/taskId/authoritativeTasks | 0.99 | consumer text может измениться | Нет: additive actionable diagnostic |
| Rollout | user | локальный код, без публикации/установки | 1.0 | активные агенты пока используют старый package | Нет: отдельная установка не поручена |

### 6.6 Runtime/data contract

| Area | Source of truth | Change | Verification |
| --- | --- | --- | --- |
| Application input | schema v1 / current source | нет | existing schema/introspection tests |
| Date validation | original/staged under existing lock | допускаем старую unchanged pair | full fixture matrix |
| Stored tasks/receipt | existing transaction | нет миграции/auto-repair | snapshots, receipt regressions |
| Installed tool | global 1.32.0 | не меняется | final report separates source from install |

## 7–9. Правила, интеграции, состояние

Единый gate PreviewAsync/TryApplyAsync в ExecuteAsync. No persistent fields/config changes. Сравнение final pair не зависит от текущей даты/локальной timezone. Другие scheduling rules не снимаются; graph write safety и precondition failure сохраняются.

## 10. Rollout / rollback

Изменение source начинает действовать в собранном CLI; published/global 1.32.0 остаётся прежним до отдельного package/install workflow. Тестировать на isolated fixtures, реальный apply не запускать. Откат локального implementation change set без destructive git reset; данных для отката нет, live mutation не выполняется.

## 11. Acceptance / обязательная валидация

| AC | Check/evidence | Negative/stop |
| --- | --- | --- |
| AC1 | create без дат, description/title и archive в пространстве с посторонним invalid interval; недатовая правка самой invalid карточки; dry-run no mutation, apply + authoritative read-back, old pair exact preserved | любая blanket rejection или auto-repair FAIL |
| AC2 | new inverted create и setField valid→invalid / legacy invalid→different invalid; error.kind/taskId, zero persisted changes всех задач пакета | accepting new inversion FAIL |
| AC3 | repair, clear begin/end, equal endpoints, distinct offsets, final-state multi-op dates | timezone/local-date assumptions FAIL |
| AC4 | affected CLI tests + ordinary affected build + full main и full headless tests + diff/scope checks | partial/red/timeout не PASS |
| AC5 | README точен; no live mutation/tool update/unrelated diff | claim installed fixed без rollout FAIL |

Acceptance-to-Test Matrix = таблица выше: каждый AC имеет automated integration check либо static/diff evidence. Новые regression tests сначала запускаются до fix: продуктовый RED должен совпасть с подтверждённой причиной. Затем targeted class, affected CLI build, full suites. Public behavior/shared storage boundary требует full suite по testing-baseline, не только быстрых тестов.

Runner TUnit/Microsoft.Testing.Platform, SDK global.json 10.0.400. Использовать repo `scripts/ci/Invoke-TargetedTestSeries.ps1` или установленный skill `run-tunit-tests` с TUnit `--treenode-filter`, не VSTest filter. По этапам restore/build/test для main/headless применять `scripts/ci/Invoke-TestStage.ps1`; перед запуском проверить SDK и параметры scripts, serial outputs не пересекать. Existing whole suite historically около 15 минут CI, локальное время не гарантировано; progress и report paths записать перед long run. Build `dotnet build src/Unlimotion.Cli/Unlimotion.Cli.csproj`. CI не заменяет local full suite без отдельного repo permission. После timeout сначала inspect progress/log и новую гипотезу, не identical retry.

Параметры repo scripts сверены, `dotnet --version` = 10.0.401 (совместимый latestPatch). После test-project build targeted run: `dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/UnlimotionCliIntegrationTests/*" --maximum-parallel-tests 1 --minimum-expected-tests 1 --output Detailed --report-trx --results-directory=<fresh artifact path>`. Новые тесты для TDD RED выбираются отдельно по имени. Full main/headless: `pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage <restore|build|test> -Project <main|headless> -ResultsRoot artifacts/cli-date-validation/<run-id>`; выполнять sequential, точные stdout/TRX/stage manifests — evidence. Проверки не запускались до approval, кроме read-only SDK preflight и ранее выполненного installed CLI diagnostic preview.

## 12. Риски / objections

| Возражение | Mitigation | Status |
| --- | --- | --- |
| «Вы молча испортили/исправили старые задачи» | никаких live writes/migration; exact preserved date pair test | mitigated |
| «Теперь можно создавать неправильные даты» | reject new/changed inversion, atomic snapshots | mitigated |
| «В исходниках исправлено, а агентам всё ещё не работает» | source/build и global package разделены; установка/публикация отдельно | explicit limitation |
| «Проверки зелёные, но прежняя ошибка не покрыта» | permanent failing regression + create/description/archive scenarios | mitigated |

## 13–18. План, профиль, альтернативы

Порядок: exact approval → local branch/preflight → failing regression → минимальный date gate fix → targeted tests → build/full suites → docs/diff/post-EXEC review. Без кодовых изменений до approval. Открытых user-owned design вопросов нет.

Профиль .NET desktop применяется к shared domain/test project: UI thread/navigation/automation IDs не меняются; affected build и full suites предусмотрены. Consumer UI gate новых UI тестов не срабатывает: пользовательский симптом CLI-only.

Было: один invalid interval блокирует любой apply. Стало: блокируются только new/changed invalid final intervals. Альтернативы: auto-clear dates меняет user data без основания; убрать gate полностью теряет safety; проверять только referenced IDs всё ещё блокирует недатовые правки legacy карточки и не выражает invariant. Original-vs-staged pair comparison выбран как минимальный совместимый gate.

## 19. Quality gate / review

### Linter

| № | Статус / evidence |
| --- | --- |
| 1 | PASS: исходный symptom/outcome |
| 2 | PASS: neighboring failures + synthetic installed reproduction |
| 3 | PASS: global final date gate как причина |
| 4 | PASS: compatibility без loss of safety |
| 5 | PASS: non-goals data/tool/publication/UI |
| 6 | PASS: responsibility/file table |
| 7 | PASS: общий Preview/TryApply ExecuteAsync gate |
| 8 | PASS: original/staged exact final pair algorithm |
| 9 | PASS: deterministic taskId/error/authoritative state |
| 10 | PASS: O(N), no I/O |
| 11 | PASS: schema/data не меняются |
| 12 | PASS: grandfather unchanged pair, source/install boundary |
| 13 | PASS: reversible source diff, no live data rollback needed |
| 14 | PASS: observable AC1–5 |
| 15 | PASS: acceptance matrix + negative fixtures |
| 16 | PASS: exact runner commands/SDK/preflight/timeout plan |
| 17 | PASS: RED→fix→targeted→build/full→review |
| 18 | PASS: decisions without open user-owned unknowns |
| 19 | PASS: expanded public behavior/shared storage boundary |
| 20 | PASS: shared .NET build/testing and UI applicability explicit |

### Rubric

| Критерий | Балл | Основание |
| --- | ---: | --- |
| Цель/границы | 5 | independently usable apply, no data/tool/publication changes |
| AS-IS | 5 | source + installed reproduction + actual neighboring failures |
| Дизайн | 5 | exact original/staged final pair gate and error shape |
| Безопасность/rollback | 5 | grandfather unchanged pair only, atomic reject, no migration |
| Тестируемость | 5 | full scenario matrix, RED→GREEN and mandatory full suites |
| Автономность | 5 | no open design decisions; explicit execution/rollout boundaries |

30/30 — не заменяет exact approval/review.

### Role-Based Review Result

| Role | Question | Verdict |
| --- | --- | --- |
| Domain workflow | сохранены старые данные и разрешены independent tasks? | self contract PASS |
| Tester | negative/transient/final/null/offset cases и full suite? | self plan PASS |
| Architect | gate под lock, final state, error shape без schema change? | self contract PASS |
| Delivery/security | no live mutation/tool install/publication claim? | self boundaries PASS |
| UX/copy | сообщение даёт taskId и даты? | CLI output plan PASS, visual не применим |

### Post-SPEC review

Scope/Evidence pass: central owners/template, consumer override, TaskApplicationCommandService original/staged/error model, CLI integration helpers, schema/help, actual neighboring previews и synthetic 1.32.0 reproduction. Contract pass: AC1–5 соответствуют исходному симптому, безопасность/rollout разделены. Adversarial pass: добавлены same-card non-date edit, different-invalid pair, nullable exact comparison, transient invalid/final valid и deterministic first error. Depth checklist: unrelated SPEC исключена; claims source vs install разделены; no UI code; graph/precondition/receipt границы не меняются; docs validate scope уточняется; manual challenge — автолечение/пропуск new inversion покрывается отрицательными tests. Self findings: нет после уточнения exact offset equality и same-card exemption. No-findings justification: алгоритм покрывает все строки state matrix без снятия новых validation ошибок; тесты проверяют persist, не только success. Reviewer нашёл два MEDIUM: невозможный change-and-revert одного поля противоречил duplicate-target gate, а shorthand graph-only неточно описывал validate. Исправлены текст алгоритма (duplicate-target rejection сохранён, no-op допускается) и точная граница validate (load/duplicates/references/availability, не planned dates). Effective sandbox reviewer danger-full-access/unrestricted, только чтение; writable adversarial fallback, не read-only review. Targeted re-review ожидается; до его результата ready approval не объявляется.

### Post-EXEC review

Implementation review выявил HIGH (timezone-dependent fixture offsets) и MEDIUM (неполный диагностический контракт tests). HIGH исправлен: persisted baseline после SaveTasks, no-op от него, alternate offset относительно baseline, AC3 проверяет instants. MEDIUM исправлен: обе точные O даты в message, authoritative id/исходные dates, отдельный reversed-order ordinal error test. README и SPEC уточняют effective snapshot vs textual file offset. Targeted re-review PASS, новых actionable findings нет. Effective sandbox unrestricted/danger-full-access; writable adversarial fallback, только чтение. Final post-EXEC verdict PASS: reviewer независимо сверил TRX 17/17, 65/65, 1147/1147, 51/51 без failed/skipped, stage manifests exit0, diff/scope. Source-ready, не installed-ready. Nonblocking LOW о stale pending-full-suite wording закрыт этой правкой.

### Review findings

Final post-SPEC stop verdict: PASS. Targeted reviewer re-review подтвердил оба исправления по source contracts; ожидание review завершено, можно запросить exact approval. Scope/Evidence, Contract, Adversarial и Role-Based passes выше сохранены; обязательные EXEC проверки ещё не выполнены и не называются PASS.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | request contract | change-and-revert одного поля невозможно из-за duplicate-target gate | сохранить ConflictingOperations, описать разные поля/no-op | fixed; targeted re-review PASS |
| MEDIUM | docs contract | graph-only shorthand недостаточно точен для validate.isValid | перечислить load/duplicates/references/availability, исключить planned dates | fixed; targeted re-review PASS |
| HIGH | EXEC test portability | fixed +03 fixture не равен загруженному offset на другом timezone | baseline LoadTask, dynamic no-op/offset, instant AC3 assertions | fixed; targeted re-review PASS |
| MEDIUM | EXEC diagnostics | недостаточные assertions message/authoritative/order | обе O даты, original id/dates, reversed-order two-error test | fixed; targeted re-review PASS |

## Approval

Exact approval получено 01.10.2026: «Спеку подтверждаю». EXEC разрешён в пределах этой SPEC, без live task mutation, global tool update или публикации.

## 20. Журнал действий агента

| Фаза / событие | Решение / основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| Diagnose | root cause confirmed без изменения live tasks | neighboring failures, synthetic clean PASS/inverted FAIL, 3 old inversions read-only | correction request | «Исправь» | diagnostic temp fixtures, source |
| SPEC | expanded public application compatibility fix, no automatic data cleanup | final pair algorithm + AC1–5 + self-review; reviewer pending | review, exact approval | approval ещё нет | эта SPEC |
| SPEC / review rework | исправлены два MEDIUM по duplicate-target и validate boundary | source contract сохранён, формулировки уточнены; targeted re-review pending | получить verdict и exact approval | не требовалось | эта SPEC |
| SPEC / review closure | targeted re-review закрыл обе находки | post-SPEC PASS; код/tests/README не менялись | запросить exact approval | ожидается «Спеку подтверждаю» | эта SPEC |
| EXEC / approval | exact gate пройден; создана local fix/cli-legacy-date-validation от 116f2a4f | SDK10.0.401; unrelated SPEC сохранена | добавить failing regression и подтвердить RED | «Спеку подтверждаю» | эта SPEC, tests |
| EXEC / RED | 16 cases: 12 FAIL, 4 PASS; 11 failures подтверждают product date gate/taskId, 1 — ошибочное ожидание текстового offset | existing storage normalization characterized; product RED подтверждён | минимальный gate fix | в пределах approval | artifacts/cli-date-validation/20261001-red-tests |
| EXEC / invalid run | tests ошибочно стартовали до build completion и использовали старые DLL; Windows copy lock вызвал build failure | этот run не считается GREEN/evidence fix; после завершения тестов serial rebuild PASS | strictly sequential runs | не требовалось | 20261001-green-build, 20261001-green-tests |
| EXEC / review fixes | persisted baseline/diagnostics/order assertions и archive status precondition уточнены | reviewer HIGH+MEDIUM closed, sequential build PASS | targeted tests | не требовалось | 20261001-reviewed-build |
| EXEC / GREEN | 17/17 regression cases PASS, 0 skipped, 33s | preview/write/readback/atomic reject/receipt/order covered | full CLI class, affected build, main/headless | не требовалось | artifacts/cli-date-validation/20261001-reviewed-tests |
| EXEC / CLI class/build | 65/65 CLI integration tests PASS, 0 skipped, 112s; ordinary CLI Debug build PASS, 0 warnings/errors | affected regressions/CLI boundary checked | full main/headless | не требовалось | artifacts/cli-date-validation/20261001-cli-class |
| EXEC / full main | 1147/1147 PASS, 0 skipped, 843s, exit0 | full shared application/storage/UI regression suite GREEN | full headless, final review/scope | не требовалось | artifacts/cli-date-validation/20261001-full/main |
| EXEC / full headless | restore/build PASS; 51/51 tests PASS, 0 skipped, 453s, exit0 | independent headless regression suite GREEN | final review/scope | не требовалось | artifacts/cli-date-validation/20261001-full/headless |

## EXEC acceptance evidence

AC1–AC3 PASS: `20261001-reviewed-tests` TRX содержит 17 passed, 0 failed/skipped; `20261001-cli-class` — 65 passed, 0 failed/skipped. Старая invalid пара из persisted baseline не блокирует independent/create/title/description/archive, same-card edits и exact no-op; new/changed/offset-only invalid pair атомарно отклоняется с ID, обеими O датами и исходным snapshot. Repair/clear/equal-instant/final-state multi-field move и ordinal first error проверены.

AC4 PASS: sequential reviewed main build (51 existing warnings, 0 errors), ordinary `dotnet build src/Unlimotion.Cli/Unlimotion.Cli.csproj -c Debug --no-restore` (0 warnings/errors), full main 1147/1147 и full headless 51/51 (0 skipped); restore/build headless PASS (0 warnings/errors). Полные TRX, invocation metadata и stage manifests: `artifacts/cli-date-validation/20261001-full/{main,headless}`. Failed concurrent old-DLL run выше сохранён и не используется как validation evidence.

AC5 PASS: README отражает effective date-pair exemption, diagnostics, final-state validation и validate scope; source diff ограничен TaskApplicationCommandService, CLI integration tests, CLI README и этой SPEC. `git diff --check` PASS. Пользовательская `specs/2026-09-28-task-importance-value-visibility.md` не изменена. Live tasks не записывались; installed `unlimotion.cli` остаётся 1.32.0; commit/push/PR/install/release не выполнялись.

Self-review closure: scope/evidence, contract и adversarial passes согласованы с AC1–AC5; оба post-EXEC finding fixed и targeted re-review PASS. Production change остаётся под существующим lock и до persistence; graph validation, preconditions, duplicate-target conflict, receipt/recovery/schema не менялись. No-findings justification: старые данные не auto-repair, новые нарушения не bypass, конечное состояние/deterministic error покрыты regression tests; полные suites GREEN. Source-ready, не installed-ready; rollout требует отдельного поручения.

## Authorized local rollout / PR delivery

После source-ready пользователь отдельно поручил: «Обнови установленный cli и после оформи pr». Это разрешило local tool update и commit/push/PR; публичный release/NuGet publish/merge не поручены. Предыдущие ограничения и installed-ready status выше описывают момент завершения implementation, не итог этого rollout.

01.10.2026 remote main после fetch остаётся 116f2a4f, совпадает с базой ветки. Release package `Unlimotion.Cli.1.32.1-local.20261001.nupkg` собран из проверенного source diff; SHA256 `74498B6200480CDD72C4A1DAD89BC512FF6FCF083A25DBE8FD82E683C19F6E1D`. Package identity явно local prerelease, не объявляет стабильный публичный релиз. Сначала staged tool install и preview PASS, затем global `dotnet tool update` с локальным source обновил 1.32.0 → 1.32.1-local.20261001.

Installed command `version --format json`: packageVersion=1.32.1-local.20261001, applicationVersion=1.32.1.0, buildKind=package. `context` успешно разрешает desktopSettings projection. Через обычный global shim preview/write/read-back в isolated fixture PASS; старая inversion сохранена, новая задача создана, receipt записан. Fixture/request/package сохранены в `artifacts/cli-date-validation/20261001-install`, не публикуются с source. Рабочее task space не использовалось для записи.

Rollback installed tool: `dotnet tool update --global Unlimotion.Cli --version 1.32.0 --allow-downgrade --source https://api.nuget.org/v3/index.json`. Source rollback — revert одного fix commit; task migration отсутствует.

Installed-command preview того же create request в active task space PASS: success=true, mode=preview, didMutate=false, receiptWritten=false, projected taskCount=2984. Это подтверждает устранение blanket date rejection и на реальном графе, без записи тестовой задачи.
