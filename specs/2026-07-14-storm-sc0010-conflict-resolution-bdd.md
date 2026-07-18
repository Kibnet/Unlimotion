# SPEC: Исполняемый BDD-мост разрешения Git-конфликтов (SC-0010-003)

## 0. Метаданные
- Тип (профиль): `storm-product-development`, delivery-task, test-only executable BDD.
- Владелец: STORM `/storm:cover` для `ST-0010`.
- Масштаб: small.
- Целевое семейство / behavior baseline: `SC-0010-003`, `GR-030`, `AC-0030`.
- Поверхность: Не применимо. Продуктовый UI и workflow не меняются.
- Effective runtime: .NET 10, TUnit; model/runtime не влияет на продуктовый результат.
- Eval baseline / evidence: две существующие passing `BackupViaGitServiceTests`; новый bridge должен исполнять четыре шага Gherkin.
- Целевой релиз / ветка: `storm-bootstrap`.
- Ограничения: не менять production code, `.feature`, существующие tests, test annotations, UI, конфигурацию, Git remotes или данные пользователя.
- Связанные ссылки: `features/storm/st-0010-git-backup-sync.feature`, `docs/product/storm.json`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`.

## 1. Overview / Цель
Сделать `SC-0010-003` исполняемой BDD-спецификацией, сохранив текущие product behavior и регрессионные тесты как источник фактического evidence.

Outcome contract:
- Success means: сценарий запускается из feature text через `SD-0143..SD-0146`, а файл- и field-level конфликтные решения доказаны существующими тестами.
- Итоговый артефакт / output: новый `TS-0062`, тестовый контракт, step definitions и синхронизированные STORM artifacts/reports.
- Stop rules: остановиться и оформить отдельную QUEST delivery SPEC, если для proof потребуется изменить product code, существующие тесты, annotations или `.feature`.

## 2. Текущее состояние (AS-IS)
- `SC-0010-003` автоматизирован и связан с `TS-0008/TS-0009`, но не имеет step definitions и не исполняется из Gherkin.
- `BackupViaGitServiceTests.ResolveConflict_UseCurrentVersion_CommitsAndPushesResolution` доказывает разрешение конфликта по файлу с commit/push.
- `BackupViaGitServiceTests.ResolveConflictFields_UsesSelectedVersionsAndMergedFields` доказывает выбор current/incoming/merge по полям и commit/push merged результата.
- Предыдущие slices `SC-0010-001/002` задали repo-local runner pattern; `ST-0010` покрыта 2/4 executable scenarios.

## 3. Проблема
Существующее evidence конфликтов не связано с текстом `SC-0010-003`, поэтому `/storm:cover` не может считать этот сценарий step-executable.

## 4. Цели дизайна
- Переиспользовать два существующих passing regression tests без изменения их annotations.
- Изолировать orchestration в новом test-only contract.
- Сохранить Gherkin и acceptance criteria как независимые product artifacts.
- Сделать каждое выполнение воспроизводимым отдельной TUnit-командой.

## 5. Non-Goals (чего НЕ делаем)
- Не реализовываем новое conflict-resolution поведение.
- Не меняем UI `ConflictResolutionControl`, ViewModels, remote repositories, jobs или Git configuration.
- Не включаем UI тесты или video evidence: UI behavior/layout не меняются.
- Не заявляем full-suite pass: известный полный запуск не имеет итоговой сводки из-за timeout.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `GitConflictResolutionContract.cs` -> последовательно выполняет два существующих Git-conflict proof и возвращает результат.
- `GitConflictResolutionStepDefinitions.cs` -> связывает точные шаги `SC-0010-003` с контрактом.
- `StormGitConflictResolutionExecutableSpecTests.cs` -> парсит feature, проверяет tags/rule/steps и запускает runner.
- `StormStepDefinition.cs` -> хранит только новый scenario context.
- `storm.json` и шесть reports -> фиксируют `TS-0062`, `SD-0143..SD-0146`, статус и metrics.

### 6.2 Детальный дизайн
- Контракт создаёт disposable `BackupViaGitServiceTests`, вызывает оба точных метода в одном сценарном прогоне и гарантирует `Dispose()` в `finally`.
- `GitConflictResolutionScenarioResult` хранит отдельные `FileResolutionPassed` и `FieldResolutionPassed`; `AssertAsync` обязан проверять оба флага, поэтому успешное завершение одного метода не может замаскировать непроверенное второе evidence.
- `SD-0143` и `SD-0144` устанавливают/проверяют контекст набора задач и истории `ST-0010`.
- `SD-0145` запускает contract для точного `Когда` шага.
- `SD-0146` утверждает одновременно file-level и field-level resolution before commit/push.
- В `storm.json` новый `TS-0062` дополняет, но не заменяет, links на `TS-0008/TS-0009`; scenario получает `passing` только по фактически пройденным командам.
- Visual planning artifact и UI video evidence: Не применимо, потому что deliverable не меняет UI behavior/layout.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Git conflict resolution | Пользователь разрешает файл или поля и завершает sync | Existing behavior сохраняет выбранные данные и push-ит resolution | BDD 1/1 и два direct TUnit tests 2/2 | AC-0030 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Git merge conflict | Выбор file-level resolution | Conflict очищен, resolution commit/push выполнен | Не изменяется этой SPEC | Existing direct test |
| JSON field conflict | Выбор current/incoming/merge fields | Merged файл записан, conflict очищен, commit/push выполнен | Не изменяется этой SPEC | Existing direct test |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Evidence scope | agent | Два direct Git tests как достаточный proof AC-0030 | 0.94 | UI-specific representation не исполняется bridge-ом | Нет |
| Изменение existing tests | agent | Запрещено, вызываем их без изменений | 1.00 | Нет | Нет |
| UI automation | agent | Не применимо к test-only bridge | 0.98 | Не покрывает новый UI, которого нет | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Git conflict data | Existing temporary local repositories in fixture | Нет | Не применимо | Два isolated TUnit tests |
| Product configuration | Existing `BackupViaGitService` configuration fixture | Нет | Не применимо | Contract invokes existing tests |

## 7. Бизнес-правила / Алгоритмы
- Сценарий passing только когда `FileResolutionPassed` и `FieldResolutionPassed` истинны, а оба direct tests подтверждают commit/push.
- File-level resolution не подменяет field-level resolution, и наоборот.
- Никакой production contract не меняется.

## 8. Точки интеграции и триггеры
- `StormFeatureParser` читает `SC-0010-003` из existing feature.
- `StormScenarioRunner` вызывает `GitConflictResolutionStepDefinitions`.
- Новый `TS-0062` связывает result со STORM traceability.

## 9. Изменения модели данных / состояния
Только transient fields `GitConflictResolution*` в `StormScenarioContext`; persisted product state отсутствует.

## 10. Миграция / Rollout / Rollback
Миграция и rollout не требуются. Откат ограничен удалением новых test-only файлов и artifact links; product code и пользовательские данные не затронуты.

## 11. Тестирование и критерии приёмки
1. `StormGitConflictResolutionExecutableSpecTests` исполняет четыре точных шага `SC-0010-003` с `SD-0143..SD-0146`.
2. Contract возвращает два независимых флага и падает, если file-level или field-level resolution evidence не проходит.
3. `TS-0062`, scenario, rule и reports синхронизированы; `ST-0010` становится 3/4 step-executable, общий ratio 37/45.
4. Production code, `.feature`, existing tests и annotations остаются неизменными.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-0030, file-level resolution before commit/push | `ResolveConflict_UseCurrentVersion_CommitsAndPushesResolution` | Не применимо | TUnit output | Автоматизировано |
| AC-0030, field-level resolution before commit/push | `ResolveConflictFields_UsesSelectedVersionsAndMergedFields` | Не применимо | TUnit output | Автоматизировано |
| Gherkin binding | `StormGitConflictResolutionExecutableSpecTests` | `validate-artifacts.py` | TUnit + STORM report | Автоматизировано |

Команды проверки:
```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/StormGitConflictResolutionExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/BackupViaGitServiceTests/ResolveConflict_UseCurrentVersion_CommitsAndPushesResolution" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/BackupViaGitServiceTests/ResolveConflictFields_UsesSelectedVersionsAndMergedFields" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски и edge cases
- Direct tests create temporary local Git repositories. Mitigation: each fixture owns and disposes its temporary root.
- Full suite remains unconfirmed because prior full run timed out; the claim is limited to targeted proof.
- Generic scenario steps add expected duplicate-step lint warnings. Mitigation: record intentional scenario-specific `SD` bindings and only accept validator warnings with 0 errors.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Bridge may overstate product coverage | It wraps tests rather than adding behavior | Scenario state is `passing` only after separate BDD and direct evidence; links to original tests remain | mitigated |
| UI conflict dialog is not tested | AC refers to user workflow | No UI behavior changes; scope is evidence binding, not UI delivery | mitigated |
| Full regression pass is missing | Prior suite timed out | Explicitly excluded from PASS claim and recorded as residual risk | mitigated |

### Rework Prevention Checklist
- User-visible conflict outcomes have direct file/field evidence.
- Acceptance criteria remain intact and Gherkin remains the bridge input.
- All autonomous choices are in the Decision Ledger.
- Role-based review is required before EXEC.

## 13. План выполнения
1. Добавить contract, step definitions, executable spec test и minimal scenario context.
2. Выполнить targeted BDD/direct tests и build.
3. Выполнить `/storm:bdd-sync`, `/storm:bdd-lint`, обновить six reports and metrics.
4. Провести post-EXEC review, исправить только confirmed findings, проверить diff и создать Conventional Commit.

## 14. Открытые вопросы
Нет. Текущая active workflow policy автоматически подтверждает SPEC после PASS review.

## 15. Соответствие профилю
- Профиль: `storm-product-development`.
- Выполненные требования профиля: Russian product artifacts, preserved acceptance criteria, repo-local executable BDD, explicit evidence, no code/feature/annotation changes without separate SPEC, BDD sync/lint and coverage refresh.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-14-storm-sc0010-conflict-resolution-bdd.md` | Новая SPEC | Управление test-only delivery |
| `src/Unlimotion.Test/GitConflictResolutionContract.cs` | Новый contract | Переиспользовать two direct tests |
| `src/Unlimotion.Test/StormBdd/GitConflictResolutionStepDefinitions.cs` | Новые step definitions | Исполнить Gherkin |
| `src/Unlimotion.Test/StormGitConflictResolutionExecutableSpecTests.cs` | Новый executable spec | Проверить parser/runner binding |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Minimal context | Хранить transient result |
| `docs/product/storm.json`, шесть reports | Traceability и metrics | `/storm:bdd-sync` и `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| SC-0010-003 | linked-only automated scenario | executable/passing BDD с `TS-0062` |
| ST-0010 | 2/4 executable scenarios | 3/4 executable scenarios |
| Product code / feature / annotations | Существующее поведение | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: изменить `.feature` или добавить новые UI tests.
- Плюсы: отдельные новые UI proof points.
- Минусы: меняет существующие artifacts/automation без product need.
- Почему выбранное решение лучше: два существующих isolated Git tests полностью соответствуют file/field and commit/push contract, а bridge только делает связь исполняемой.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Scope, AS-IS, outcomes и stop rules заданы |
| B. Качество дизайна | 6-10 | PASS | Files, state, evidence contract и decision ledger конкретны |
| C. Безопасность изменений | 11-13 | PASS | Production/UI/config/annotations out of scope; rollback ограничен test-only files |
| D. Проверяемость | 14-16 | PASS | Каждый AC имеет exact TUnit command и STORM validator |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет открытых user decisions; review finding исправлен |
| F. Соответствие профилю | 20 | PASS | `storm-product-development` и local UI override соблюдены |

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Только SC-0010-003, явные non-goals и stop rules |
| 2. Понимание текущего состояния | 5 | Прочитаны Gherkin, existing tests и previous slices |
| 3. Конкретность целевого дизайна | 5 | Названы files, SD/TS IDs и двухфлаговый contract |
| 4. Безопасность (миграция, откат) | 5 | Нет persisted/product changes; test-only rollback |
| 5. Тестируемость | 5 | BDD 1/1, два direct tests 2/2, build, validator и diff gate |
| 6. Готовность к автономной реализации | 5 | Нет неразрешённых решений или скрытого scope |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Покрыты ли file/field decisions до commit/push? | PASS | Two direct tests exactly cover both decisions and remote result |
| UX / designer | not applicable | UI не меняется | Не применимо | Нет |
| Tester / validation | applicable | Есть ли reproducible evidence для каждого AC? | PASS | Exact one-test TUnit filters avoid unsupported combined filter syntax |
| Developer / architect | applicable | Изолирован ли bridge от production behavior? | PASS | New contract uses disposable existing fixture; no production references change |
| Delivery / operations / security | applicable | Не затрагиваются ли remotes, credentials и config? | PASS | Only fixture-local repositories; no external remotes, secrets or config writes |

### Post-SPEC Review
- Статус: PASS после исправления.
- Scope reviewed: this SPEC, `SC-0010-003`, `GR-030`, `AC-0030`, both direct evidence methods and prior SC-0010 bridge pattern.
- Decision: workflow auto-approval permits EXEC.
- Review passes: Scope/Evidence PASS; Contract PASS after adding explicit two-flag assertion; Adversarial risk PASS; Role-Based PASS; re-review PASS.
- Evidence inspected: feature scenario, existing tests at `BackupViaGitServiceTests.cs:560` and `:864`, local template and STORM profile.
- No-findings justification: remaining risks are explicitly bounded to targeted tests and prior full-suite timeout; no behavior change is planned.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Draft did not require independently assertable file/field result states. | Add explicit result flags and `AssertAsync` requirement. | fixed |

### Post-EXEC Review
- Статус: PASS после исправления artifact finding.
- Scope reviewed: approved SPEC, `git status --short`, changed test-only files, `storm.json`, six reports, targeted validation evidence and production/feature diff.
- Decision: можно коммитить результат.
- Review passes: Scope/Evidence PASS; Contract PASS; Adversarial risk PASS; Role-Based PASS; re-review PASS after restoring unrelated scenario.
- Evidence inspected: Release build (69 existing warnings, 0 errors), executable BDD 1/1, direct file/field conflict-resolution tests 2/2, STORM validator 0 errors/15 warnings/37 of 45, `git diff --check`.
- Validation evidence: UI video evidence не применимо, потому что продуктовые UI behavior/layout не менялись.
- Unrelated changes: `SC-0012-002` кратко получил `TS-0062` из-за повторяющегося JSON hunk, затем полностью восстановлен и структурно проверен как `automated`, без step definitions и только с `TS-0008/TS-0009`.
- Residual risks / follow-ups: full `Unlimotion.Test` suite не заявляется passing из-за предыдущего 304-second timeout without summary; next `/storm:cover` candidate is `SC-0010-004`.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | unrelated changes | Repeated artifact hunk temporarily changed `SC-0012-002`. | Restore its original status/tests/steps and validate parsed JSON. | fixed |

## Approval
Автоматическое подтверждение действующей workflow policy после PASS review; явная фраза: "Спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создать test-only BDD bridge для SC-0010-003 | 0.94 | Нет | Провести role-based review | Нет | Автоподтверждение по active workflow | Два existing direct Git tests соответствуют AC-0030 | Эта SPEC |
| SPEC | Исправить contract evidence model и завершить review | 0.98 | Нет | Перейти к EXEC | Нет | Автоподтверждение по active workflow | Два флага исключают частичное доказательство сценария | Эта SPEC |
| EXEC | Реализовать, проверить и синхронизировать SC-0010-003 | 0.98 | Нет | Закоммитить и начать SPEC SC-0010-004 | Нет | Автоподтверждение по active workflow | BDD 1/1 и direct evidence 2/2 прошли; scope drift восстановлен до review gate | Test-only files, storm.json, six reports |
