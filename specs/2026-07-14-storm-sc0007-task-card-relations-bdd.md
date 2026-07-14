# STORM SC-0007-002: executable BDD для блоков отношений карточки задачи

## 0. Метаданные

- Статус: `DONE` (post-SPEC и post-EXEC review `PASS` с зафиксированным residual validation risk)
- Тип (профиль): `delivery-task`, test-only executable BDD implementation + artifact sync
- Владелец: Codex, автоматическое подтверждение active STORM goal после `PASS` post-SPEC review
- Масштаб: `small`
- Целевая модель: `gpt-5.5`
- Целевой релиз / ветка: локальная ветка `storm-bootstrap`
- STORM scope: `ST-0007 / AC-0020 / GR-020 / SC-0007-002`
- Central stack: `model-behavior-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `storm-product-development`, `review-loops`
- Ограничения: не менять `src/Unlimotion/**`, XAML, `.feature`, automation IDs, existing test annotations, `.csproj`, CI/workflows или persisted schema.
- Связанные ссылки: `features/storm/st-0007-task-card.feature`, `docs/product/storm.json`, `MainControlRelationPickerUiTests`, `MainWindowViewModelTests`.

## 1. Overview / Цель

Сделать `SC-0007-002` исполняемым из текущего feature text. Новый TUnit bridge должен доказать, что пользователь может открыть blocks `parents`, `containing`, `blocked-by` и `blocked`, а подтверждённые изменения поддерживают направленные обратные persisted links.

Outcome contract:

- Success means: scenario проходит через четыре новые repo-local step definitions; новый `TS-0052` связан с `SC-0007-002`; existing `TS-0005`/`TS-0008` сохранены.
- Итоговый артефакт / output: test-only contract, `SD-0103..SD-0106`, синхронизированные STORM reports и measurable `27/45` executable scenarios только после passing evidence.
- Stop rules: остановиться и оформить отдельный delivery-task через QUEST, если evidence требует изменения production/UI/XAML, feature wording, annotations или persisted contract.

## 2. Текущее состояние (AS-IS)

- `SC-0007-002` имеет `status: automated`, links `TS-0005` и `TS-0008`, но `step_definitions: []`; текущая executable coverage равна `26/45`.
- `MainControlRelationPickerUiTests.TaskCardRelationEditor_OpenTargetsExpectedInput` открывает четыре UI route: parents, blocking (blocked-by), containing и blocked, проверяя соответствующий focus/input automation ID.
- `MainControlRelationPickerUiTests.TaskCardRelationEditor_AddParentFromCard_UpdatesStorage` подтверждает parent -> containing reverse storage link через UI picker.
- `MainWindowViewModelTests.CurrentItemParentsAdd_Success`, `CurrentItemContainsAdd_Success`, `CurrentItemBlockedByAdd_Success` и `CurrentItemBlocksAdd_Success` подтверждают направленные storage links для всех четырёх relation kinds.
- Production mapping уже определён в `MainWindowViewModel`: `Parents`/`Containing` используют `CopyInto`, `Blocking`/`Blocked` используют `Block`; UI `RelationAddButton_OnClick` открывает editor по `TaskRelationKind`.

## 3. Проблема

Существующее UI и domain evidence покрывает relation behavior, но Gherkin `SC-0007-002` не исполняется. Trace от текста scenario к четырём UI routes и к обратным links остаётся декларативным.

## 4. Цели дизайна

1. Исполнять ровно текущий текст `SC-0007-002` без правки `.feature`.
2. Сохранить distinction четырёх user-visible routes: parents, containing, blocked-by и blocked.
3. Подтвердить обратные persisted pairs: `ParentTasks/ContainsTasks` и `BlocksTasks/BlockedByTasks`.
4. Использовать существующие repository-proven test fixtures и доказательства, не меняя product behavior.
5. Обновлять reports/metrics только после passing target evidence.

## 5. Non-Goals (чего НЕ делаем)

- Не меняем правила candidate validation, relation semantics, storage format, notifications, focus behavior или layout.
- Не добавляем/редактируем existing `[Test]`, `[Arguments]` или любые test annotations.
- Не проверяем удаление relation, invalid/deadlock flow, keyboard shortcuts, visual redesign или `SC-0007-003`.
- Не заявляем full-suite PASS без итоговой summary фактического запуска.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент / файл | Ответственность |
| --- | --- |
| `StormTaskCardRelationsExecutableSpecTests.cs` | Парсит `SC-0007-002`, проверяет tags/rule/title и запускает step runner. |
| `TaskCardRelationsStepDefinitions.cs` | Реализует `SD-0103..SD-0106` только для этого scenario. |
| `TaskCardRelationsContract.cs` | Объединяет UI route assertions и existing relation-direction checks в один BDD output contract. |
| `StormStepDefinition.cs` | Получает только additive test-local fields нового scenario. |
| `storm.json` и reports | Фиксируют `TS-0052`, scenario -> test -> steps links и фактические metrics. |

### 6.2 Детальный дизайн

1. Тест читает `features/storm/st-0007-task-card.feature`, выбирает `SC-0007-002`, ожидает tags `@rule:GR-020`, `@story:ST-0007`, `@test:TS-0005`, `@test:TS-0008` и четыре feature steps.
2. `SD-0103` фиксирует наличие task set; `SD-0104` фиксирует story context; `SD-0105` запускает `TaskCardRelationsContract`; `SD-0106` утверждает result.
3. Contract последовательно запускает four-picker UI route assertions с уже существующими automation IDs и результатами focus request:
   - `CurrentTaskParentsRelationAddButton` -> `CurrentTaskParentsRelationAddInput`;
   - `CurrentTaskContainingRelationAddButton` -> `CurrentTaskContainingRelationAddInput`;
   - `CurrentTaskBlockingRelationAddButton` -> `CurrentTaskBlockingRelationAddInput`;
   - `CurrentTaskBlockedRelationAddButton` -> `CurrentTaskBlockedRelationAddInput`.
4. Contract затем запускает existing directed-link checks для `Parents`, `Containing`, `Blocking` и `Blocked`; assertions проверяют обе стороны relationship в storage и completion impact для blocker link.
5. Visual planning artifact: не применимо, UI behavior/layout не меняются. UI video evidence: не применимо; fallback — passing Avalonia.Headless picker/storage tests и BDD bridge.
6. Ошибки/timeout не подавляются: failure любого route или reverse-link assertion делает scenario failing. Изменения запускаются последовательно из-за shared headless state.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Добавление родителя | Нажать `parents` add | Открывается parent picker; после confirm parent и child видят обратные links. | UI route + `CurrentItemParentsAdd_Success` | AC-0020 |
| Добавление содержащей задачи | Нажать `containing` add | Открывается containing picker; после confirm container и child синхронны. | UI route + `CurrentItemContainsAdd_Success` | AC-0020 |
| Добавление blocked-by / blocked | Нажать соответствующий add | Открывается верный picker; после confirm blocker/blocked storage links согласованы. | Two UI routes + two blocker checks | AC-0020 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Открыта task card с допустимым current task | Нажат relation add | `CurrentRelationEditor` открывает editor нужного `TaskRelationKind`, input получает focus request. | Candidate filter/invalid confirm не входит в slice. | Все четыре kinds проверяются. |
| Candidate selected | Confirm | Storage получает направленную связь и обратную коллекцию. | Existing validation исключает cycle/duplicate; не меняется. | Parent pair и blocker pair имеют разные направления. |
| Shared headless fixture | Contract запускает checks | Проверки идут последовательно. | Parallel execution запрещено текущими fixture attributes. | Не меняет existing annotations. |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | ---: | ---: | --- | --- |
| Scope relation kinds | agent | Все четыре kinds из AC, а не только parent. | 0.95 | Неполное AC evidence. | Нет |
| Reuse test evidence | agent | Reuse existing fixture/test methods через новый contract, следуя established contracts. | 0.88 | Contract не полностью независим от existing tests. | Нет |
| Visual artifact/video | agent | Не применимо, поскольку UI не меняется; headless evidence является fallback. | 0.95 | Нет bitmap evidence. | Нет |
| Production change | agent | Запрещён; если нужен, остановка и QUEST. | 0.99 | Scope drift. | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Production relation behavior | `MainWindowViewModel` + storage | Нет | Не применимо | Existing UI/VM tests остаются passing. |
| Persisted relation collections | Test fixture snapshots | Нет | Не применимо | Directed-link assertions в isolated fixture. |
| STORM artifacts | `docs/product/storm.json` | Additive `TS-0052`/steps/metrics | Existing links retained | Artifact validator. |

## 7. Бизнес-правила / Алгоритмы

- `Parents`: current task получает parent; parent получает containing child.
- `Containing`: current task получает child; child получает parent.
- `Blocking`: selected task является blocker current task; `BlocksTasks` и `BlockedByTasks` синхронны.
- `Blocked`: current task является blocker selected task; те же две коллекции синхронны в обратном направлении.
- Candidate validation, duplicate/cycle rejection и notifications сохраняются и не являются целью этой SPEC.

## 8. Точки интеграции и триггеры

- Feature parser -> `StormScenarioRunner` -> `TaskCardRelationsStepDefinitions` -> `TaskCardRelationsContract`.
- UI route evidence использует public existing test methods; storage evidence использует existing `MainWindowViewModelTests` isolated fixtures.
- `storm.json` связывает `SC-0007-002 -> TS-0052 -> SD-0103..SD-0106` после passing evidence.

## 9. Изменения модели данных / состояния

Только additive test-local state в `StormScenarioContext`: task-set flag, story flag и typed contract result. Production model, persisted task data и runtime state не меняются.

## 10. Миграция / Rollout / Rollback

Миграция и rollout не применимы: код production/storage не меняется. Rollback — revert одного test/artifact commit; existing test and product contracts остаются неизменными.

## 11. Тестирование и критерии приёмки

- Новый `StormTaskCardRelationsExecutableSpecTests` проходит `1/1` и исполняет точный Gherkin scenario.
- Relevant existing `MainControlRelationPickerUiTests` проходит `5/5` (four routes + parent storage mutation).
- Contract проверяет four directed VM methods, не меняя их annotations.
- Build `Unlimotion.Test` проходит без errors; validator даёт `0 errors`.
- Full `Unlimotion.Test` запускается после targeted gates только если устранён текущий process/timeout infrastructure blocker; без итоговой summary PASS не заявляется.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| Parents и containing доступны и меняют обратные links | `TS-0052`; `CurrentItemParentsAdd_Success`; `CurrentItemContainsAdd_Success` | Не требуется: UI behavior не менялся. | Targeted TUnit output, `TS-0052`. | Не применимо |
| Blocking и blocked доступны и меняют обратные links | `TS-0052`; `CurrentItemBlockedByAdd_Success`; `CurrentItemBlocksAdd_Success` | Не требуется. | Targeted TUnit output, `TS-0052`. | Не применимо |
| Каждый relation picker открывает верный UI control | `MainControlRelationPickerUiTests.TaskCardRelationEditor_OpenTargetsExpectedInput` | Не требуется. | Targeted class output. | Не применимо |
| Trace/metrics корректны | Artifact validator, bdd sync/lint reports | JSON/report review. | `storm.json`, reports. | Не применимо |

Проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskCardRelationsExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlRelationPickerUiTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски и edge cases

- Reusing existing test methods keeps the bridge small but can hide a mismatch between UI route and domain assertion; contract retains both sets as distinct evidence.
- Relation tests use file fixtures and Avalonia headless state; sequential execution and existing attributes are mandatory.
- Full suite has an already observed timeout/process-cleanup residual risk; no process termination outside approved scope.

### Expected User Review Objections

| Возможное замечание | Почему ожидаемо | Предотвращение в SPEC | Статус |
| --- | --- | --- | --- |
| «Проверен только parent» | Existing UI mutation test focuses on parent. | Contract additionally invokes containing, blocking и blocked directed-link tests plus all four UI routes. | mitigated |
| «Bridge лишь дублирует tests» | Existing tests уже являются evidence. | Feature parser и scenario-specific steps создают auditable trace, а UI/domain facts остаются отдельными. | mitigated |
| «В срез случайно попал production refactor» | Relation behavior чувствителен к storage. | Strict file scope/Non-Goals и post-EXEC diff review. | mitigated |

### Rework Prevention Checklist

- [x] Scenario, AC, rule, tags и current reports прочитаны.
- [x] Каждый user-visible relation route имеет test evidence.
- [x] Agent decisions и expected objections зафиксированы.
- [x] Production-change stop rule определён.
- [x] Role-based review применим к UI, tester, developer, business и delivery ролям.

## 13. План выполнения

1. Провести post-SPEC review и устранить однозначные findings в этой SPEC.
2. Добавить BDD test, contract, steps и test-local context fields.
3. Запустить build и targeted BDD/UI gates.
4. Синхронизировать `storm.json` и reports по фактическому evidence; запустить validator, bdd sync/lint.
5. Провести post-EXEC review, исправить findings, повторить необходимые checks и создать isolated commit.

## 14. Открытые вопросы

Нет. Current feature wording, UI route tests и directed storage tests однозначно определяют test-only slice.

## 15. Соответствие профилю

`storm-product-development` сохраняет AC/Gherkin и требует scenario -> test -> steps trace. `testing-dotnet`, `dotnet-desktop-client` и local `AGENTS.override.md` требуют релевантный passing headless UI coverage. `quest-governance` выполнен отдельной SPEC, review gate и stop rules для scope expansion.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-14-storm-sc0007-task-card-relations-bdd.md` | Эта SPEC, reviews и execution evidence. | QUEST governance. |
| `src/Unlimotion.Test/StormTaskCardRelationsExecutableSpecTests.cs` | Новый executable feature bridge. | Scenario-to-steps trace. |
| `src/Unlimotion.Test/TaskCardRelationsContract.cs` | Новый combined relation route/direction contract. | AC-0020 behavior evidence. |
| `src/Unlimotion.Test/StormBdd/TaskCardRelationsStepDefinitions.cs` | `SD-0103..SD-0106`. | Executable Gherkin. |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Additive test-local context fields. | Typed contract result. |
| `docs/product/storm.json`, `docs/product/reports/*.md` | Additive links, actual metrics and reports. | STORM sync/lint/cover. |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| `SC-0007-002.step_definitions` | `[]` | `SD-0103..SD-0106` после passing evidence |
| `SC-0007-002.status` | `automated` | `passing` только после target BDD gate |
| `SC-0007-002.linked_tests` | `TS-0005`, `TS-0008` | Existing links плюс `TS-0052` |
| Executable coverage | `26/45` | `27/45` только после validator и passing evidence |

## 18. Альтернативы и компромиссы

- Написать direct UI interaction для all four storage mutations: отклонено для этого test-only trace slice, потому что existing VM tests уже подтверждают направления и такой rewrite расширяет риск без изменения product behavior.
- Добавить generic reusable relation steps: отклонено, поскольку current BDD runner deliberately records scenario-specific bindings and lint already tracks shared wording.
- Менять feature wording на kind-specific steps: отклонено по пользовательскому ограничению сохранить Gherkin.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals ограничены одним scenario. |
| B. Качество дизайна | 6-10 | PASS | Route/direction evidence, state matrix и decisions определены. |
| C. Безопасность изменений | 11-13 | PASS | Production/storage/config excluded; rollback is revert. |
| D. Проверяемость | 14-16 | PASS | AC-to-test matrix и exact commands заданы. |
| E. Готовность к автономной реализации | 17-19 | PASS | File scope и stop rules определены; open questions нет. |
| F. Соответствие профилю | 20 | PASS | STORM, TUnit, headless UI и local override учтены. |

Итог: ГОТОВО.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| Ясность цели и границ | 5 | Один rule/scenario, explicit no-product-change boundary. |
| Понимание текущего состояния | 5 | Feature, artifact, UI routes, VM direction tests и mapping inspected. |
| Конкретность целевого дизайна | 5 | `TS-0052`, four step IDs, all routes и direction pairs named. |
| Безопасность | 5 | Test/artifact only, no schema or runtime contract change. |
| Тестируемость | 5 | Targeted BDD/UI tests, build and validator mapped to AC. |
| Готовность к автономной реализации | 5 | No user decision or ambiguous product choice. |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result

| Роль | Применимость | Вопрос review | Вердикт | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Все четыре relation directions отражают AC-0020? | PASS | Нет |
| UX / designer | applicable | User-visible picker states и fallback visual evidence определены? | PASS | Нет, UI не меняется. |
| Tester / validation | applicable | Есть ли evidence для routes, reciprocal links и failure semantics? | PASS | Нет |
| Developer / architect | applicable | Boundaries и reuse established test pattern coherent? | PASS | Нет |
| Delivery / operations / security | applicable | Нет ли CI/config/secrets/runtime risk? | PASS | Full-suite timeout остаётся declared residual risk. |

### Post-SPEC Review

| Pass | Evidence inspected | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| Scope/Evidence | Feature, `storm.json`, picker/UI/VM tests, central stack | Initial candidate мог бы доказать только открытие editors, но не mutation direction. | Явно добавить all four directed storage checks в contract. | FIXED |
| Contract | AC-0020, `TaskRelationKind` mapping, existing test contracts | «blocked-by» UI tag использует enum `Blocking`; это может запутать implementation. | Зафиксировать label-to-enum mapping в detailed design и business rules. | FIXED |
| Adversarial risk | Existing test annotations, full-suite evidence | Directly cloning four UI mutations чрезмерно расширяет small trace slice. | Reuse existing isolated direction tests; retain all four UI routes. | FIXED |
| Role-Based | BA, UX, tester, developer, delivery | Нет открытых user-owned decisions. | Не требуется. | PASS |
| Fix and re-review | Updated sections 6, 7, 11, 18 | Route visibility и reverse-link evidence now cover the full AC. | Не требуется. | PASS |

Depth checklist: scope не включает product code; every visible route has evidence; no unsupported full-suite claim; rollback/no config change explicit; likely review objections answered; manual-review challenge закрыт комбинацией UI route и direction evidence.

Stop decision: PASS. BLOCKER/HIGH findings отсутствуют; user-owned selection не требуется.

### Post-EXEC Review

| Pass | Evidence inspected | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| Scope/Evidence | `git diff`, file scope, feature, `storm.json`, six reports | Изменения ограничены новым test bridge, test-local context, SPEC и STORM artifacts; production/XAML/feature/annotations не затронуты. | Не требуется. | PASS |
| Contract | `TaskCardRelationsContract`, picker routes, `MainWindowViewModelTests` direction checks | Contract объединяет four UI routes и all four directed relation checks; enum mapping `Blocking`/`Blocked` соответствует user-visible blocked-by/blocked routes. | Не требуется. | PASS |
| Tester / UI | Build, target BDD `1/1`, `MainControlRelationPickerUiTests` `5/5`, artifact validator | Full `Unlimotion.Test` не перезапускался: ранее он превысил 304 s без summary, поэтому global regression PASS нельзя заявлять. | Сохранить риск; повторить full gate только в чистом process environment. | ACCEPTED RISK |
| Adversarial | `git diff --check`, validator after sync, lint output | Validator дал `0 errors`, `11 warnings`; новые warnings описывают intentional shared Given/When/ST-0007 story text. | Не требуется. | PASS |
| Fix and re-review | Updated reports, metrics and metadata | Все derived reports называют `SC-0007-002`; metrics согласованы: `27/45`, `106` steps, `109/109` reuse. | Не требуется. | PASS |
| Artifact freshness | `process_audit.last_exec` | Производная запись всё ещё указывала на старый `SC-0005-001`. | Синхронизировать её с текущей SPEC, validation и residual full-suite risk. | FIXED |

Итог post-EXEC: PASS для `SC-0007-002` и artifact sync. Residual risk ограничен отсутствием текущей итоговой summary полного `Unlimotion.Test`; targeted BDD/UI evidence прошло и не маскирует этот риск.

## Approval

Пользовательский goal содержит автоматическое подтверждение каждой подготовленной и прошедшей review SPEC. После `PASS` post-SPEC review эта SPEC считается подтверждённой для EXEC без отдельного ожидания.

## 20. Журнал действий агента

1. После коммита `86fb929` прочитаны current `storm.json`, `SC-0007-002`, Gherkin feature, relation picker tests, existing relation BDD contracts и production mapping.
2. Выбран следующий ranked gap `SC-0007-002`; existing coverage составляет `26/45` step-executable scenarios.
3. SPEC ограничила implementation test/artifact slice и зафиксировала automatic SPEC approval active goal.
4. После post-SPEC review добавлены `TS-0052`, `TaskCardRelationsContract`, `SD-0103..SD-0106` и additive test-local context fields; production code, XAML, `.feature`, automation IDs и existing annotations не менялись.
5. Build прошёл с 69 existing warnings и без errors; targeted executable BDD прошёл `1/1`, preserved `MainControlRelationPickerUiTests` прошёл `5/5`.
6. `/storm:bdd-sync`, `/storm:bdd-lint` и validator синхронизированы: `27/45` step-executable, `109/109` reused definitions, `0 errors, 11 warnings`.
7. Full `Unlimotion.Test` не заявлен passing: сохранён earlier timeout 304 s без summary, без несанкционированного process cleanup.
