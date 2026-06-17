# STORM Cover CV-0007: Подтверждение Attachment Workflow

## 0. Метаданные
- Тип (профиль): `guided-artifact-workflow` + `storm-product-development`; QUEST SPEC gate применяется как guardrail перед изменением `docs/product/*`.
- Владелец: Product owner / Codex по подтвержденному SPEC.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка `fdba/Unlimotion`; релиз не меняется.
- Ограничения: на фазе SPEC менять только этот файл; на EXEC не менять production code, tests, test annotations, runtime behavior, API contracts, build pipeline и persisted data.
- Связанные ссылки: `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`, `docs/product/reports/stories.md`, `docs/product/reports/bdd-sync.md`, `src/Unlimotion.Domain/Attachment.cs`, `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`, `src/Unlimotion.Server.ServiceModel/Attachment.cs`, `src/Unlimotion.Server/AppModelMapping.cs`.

## 1. Overview / Цель
Актуализировать следующий `/storm:cover` item `CV-0007` без преждевременного объявления attachment workflow активной продуктовой story.

Outcome contract:
- Success means: `CV-0007` получает проверяемый artifact-only status, который честно отражает текущие evidence: attachment backend/API code есть, но актуальный пользовательский workflow не подтвержден в story/AC/UI/docs.
- Итоговый артефакт / output: обновленные STORM artifacts and reports, если SPEC будет утверждена; при отсутствии product confirmation `CV-0007` остается не covered, но переходит в явное состояние `blocked_pending_product_decision` или эквивалентное conservative state.
- Stop rules: если для покрытия нужны новые/измененные tests, test annotations, production code, API behavior, UI workflow или product owner decision о том, что attachment workflow является актуальной продуктовой поверхностью, остановиться и предложить отдельную delivery-task SPEC.

## 2. Текущее состояние (AS-IS)
- `docs/product/reports/ranking.md` называет `CV-0007` следующим uncovered coverage item, но с условием `Needs product workflow confirmation`.
- `docs/product/reports/coverage.md` фиксирует `CV-0007` как `PRODUCT-ENTRY / proposed_attachment_workflow` со статусом `proposed`.
- `docs/product/storm.json` содержит minimal test ideas:
  - `AttachmentService_GetUploadDownload_RoundTripsUserAttachment`;
  - `AttachmentMapping_PreservesAttachmentMetadataAcrossDomainAndApiMolds`.
- Найденный код:
  - `src/Unlimotion.Domain/Attachment.cs` хранит metadata: `Id`, `SenderId`, `FileName`, `UploadDateTime`, `Hash`, `Size`.
  - `src/Unlimotion.Server.ServiceModel/Attachment.cs` публикует ServiceStack routes `POST /attachments` and `GET /attachments/{id}`.
  - `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs` требует `[Authenticate]`, сохраняет uploaded file в `FilesPath`, пишет `Attachment` в RavenDB и отдает stream по id.
  - `src/Unlimotion.Server/AppModelMapping.cs` маппит `Attachment` в `AttachmentMold` and `AttachmentHubMold`.
- Не найдено подтверждения, что attachment workflow описан в текущих product stories, acceptance criteria или UI-facing scenarios.
- В рабочем дереве уже есть незавершенные изменения предыдущей STORM task; эта SPEC не должна их откатывать или расширять вне своего scope.

## 3. Проблема
Одна корневая проблема: в артефактах есть coverage candidate для attachment backend/API code, но нет подтвержденного product behavior, поэтому `/storm:cover` не может честно закрыть `CV-0007` как covered без риска выдумать продуктовую story.

## 4. Цели дизайна
- Разделение ответственности: отделить evidence о существующем code/API от product claim о пользовательском workflow.
- Повторное использование: сохранить найденные code units и minimal test ideas как future delivery hints.
- Тестируемость: не добавлять тесты на неподтвержденный workflow; если workflow подтвердится, вынести test work в отдельный `delivery-task`.
- Консистентность: сохранить STORM chain `Need/Constraint -> Story -> AC -> Scenario -> Test -> Code` и не создавать активную story без product confirmation.
- Обратная совместимость: не менять поведение продукта, API, persisted files и тестовые аннотации.

## 5. Non-Goals (чего НЕ делаем)
- Не реализуем upload/download behavior.
- Не добавляем и не меняем tests.
- Не меняем `AttachmentService`, domain models, ServiceStack DTO, AutoMapper config, RavenDB usage или UI.
- Не объявляем attachment workflow confirmed/active без явного решения product owner.
- Не удаляем attachment code и не маркируем его deprecated без отдельной cleanup SPEC.
- Не закрываем `CV-0007` как covered, если evidence остается только code-level.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `docs/product/storm.json` -> canonical status для `CV-0007`, product-entry candidates, open questions, behavior metrics.
- `docs/product/reports/coverage.md` -> человекочитаемый coverage result и next action.
- `docs/product/reports/ranking.md` -> dependency-aware next step после triage.
- `docs/product/reports/stories.md` -> список story gaps/product-entry candidates.
- `docs/product/reports/traceability.md` -> явная фиксация, что attachment code не имеет active story/scenario/test trace.
- `docs/product/reports/bdd-sync.md` and `bdd-lint.md` -> подтверждение, что scenario/test sync не нарушен.
- `features/storm/*` -> менять только если будет создан proposed/draft scenario; по conservative route можно не менять.

### 6.2 Детальный дизайн
- Поток данных:
  1. Прочитать текущий `storm.json` and reports.
  2. Сверить найденный attachment code against existing stories/AC/scenarios/tests.
  3. Если product confirmation отсутствует, обновить artifacts так, чтобы `CV-0007` был явно blocked/unconfirmed, а не выглядел как next implementable coverage gap.
  4. Если во время EXEC обнаружится уже существующий product story/scenario/test evidence, связать его artifact-only без изменения test code; если требуются test/code edits, остановиться.
- Контракты / API: не меняются.
- Output contract / evidence rules:
  - Каждый claim о workflow должен ссылаться на product artifact, user-facing docs, UI route или existing test.
  - Code-only evidence может подтверждать только technical/product-entry candidate, не user workflow.
  - Status должен различать `proposed`, `blocked_pending_product_decision`, `covered`, `deprecated` и `internal_contract_candidate`.
- Visual planning artifact для UI-facing изменений: Не применимо, потому что UI behavior не меняется и UI workflow не проектируется в этой SPEC.
- UI test video evidence для UI automation задач: Не применимо, потому что EXEC artifact-only и без UI automation changes.
- Границы сохранения поведения: runtime behavior и public API остаются без изменений.
- Обработка ошибок: если JSON validation падает, исправить только artifact consistency в рамках SPEC; если нужно изменить code/tests, остановиться.
- Производительность: не применимо, потому что не меняется runtime path.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Attachment workflow считается `covered` только если есть подтвержденная story/AC/scenario and linked evidence.
- Attachment workflow может быть `proposed` или `blocked_pending_product_decision`, если code/API есть, но product owner confirmation отсутствует.
- Existing attachment code нельзя считать deprecated только из-за отсутствия story; cleanup требует отдельной `/storm:cleanup` delivery-task.
- Minimal test ideas остаются future hints, пока не утверждена delivery-task на test/code work.

## 8. Точки интеграции и триггеры
- `/storm:cover CV-0007` запускает artifact-only triage.
- `/storm:bdd-sync` после artifact updates проверяет Scenario -> Test consistency.
- `/storm:bdd-lint` после artifact updates проверяет качество Gherkin/statuses, если scenarios менялись.
- Если пользователь явно подтверждает attachment workflow как актуальную продуктовую поверхность, следующий триггер - отдельная `/storm:expand` или `/storm:bdd-implement` SPEC.

## 9. Изменения модели данных / состояния
- Runtime data model: не меняется.
- Product artifact model: возможно добавление/уточнение полей `coverage_backlog`, `product_entry_candidates`, `open_questions`, `bdd_sync`, `coverage_analysis`.
- New stories/scenarios: не создавать active story без product confirmation. Допустим только proposed/draft artifact, если он явно помечен как неподтвержденный и не считается covered.

## 10. Миграция / Rollout / Rollback
- Rollout: artifact-only; вступает в силу через изменения `docs/product/*` and optional `features/storm/*`.
- Обратная совместимость: не затрагивает код, тесты, API, storage.
- Rollback: revert изменений этой SPEC-driven artifact sync; prior unrelated STORM changes не трогать.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. `CV-0007` в `storm.json` и reports больше не выглядит как готовая к инженерному покрытию задача без product decision.
  2. Attachment code units остаются сохранены как evidence/product-entry candidates.
  3. Если нет product confirmation, `CV-0007` остается uncovered/blocked and next action clearly asks for product decision or separate delivery-task.
  4. `storm.json` проходит STORM validator.
  5. BDD sync/lint reports не создают ложных scenario/test links.
- Какие тесты добавить/изменить: не добавлять и не изменять.
- Characterization tests / contract checks для текущего поведения: не выполнять в этой SPEC; minimal test ideas остаются для будущей delivery-task.
- Visual acceptance для UI-facing изменений: не применимо.
- UI video evidence для UI-facing фич/багфиксов: не применимо.
- Базовые замеры до/после для performance tradeoff: не применимо.
- Команды для проверки:
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - `rg -n "[ \t]+$" docs/product features/storm specs/2026-06-17-storm-cover-attachment-workflow-confirmation.md`
- Stop rules для test/retrieval/tool/validation loops:
  - Не запускать restore/workload repair.
  - Не менять tests/code при validation failure.
  - Если validator требует schema change вне product artifacts, остановиться.

## 12. Риски и edge cases
- Риск: artifact-only update может выглядеть как отказ от attachment capability. Смягчение: явно фиксировать `unconfirmed`, а не `removed/deprecated`.
- Риск: product owner ожидал подтверждение workflow словом `Продолжай`. Смягчение: SPEC требует явное подтверждение перед product claim; это сохраняет трассируемость.
- Риск: existing tests уже покрывают attachment, но не найдены быстрым поиском. Смягчение: EXEC включает targeted search before artifact edits.
- Риск: feature files получат draft scenario without evidence и ухудшат lint. Смягчение: не добавлять scenario, если он не нужен для honest coverage state.

## 13. План выполнения
1. Перечитать `storm.json`, coverage/ranking/stories/bdd-sync/bdd-lint reports and targeted attachment code/tests.
2. Выбрать conservative artifact result:
   - если product confirmation отсутствует: `CV-0007 = blocked_pending_product_decision` или эквивалентный conservative state;
   - если existing product evidence найдено: связать artifact-only, не меняя tests/code;
   - если нужны tests/code: остановиться и предложить delivery-task SPEC.
3. Обновить `storm.json` and affected reports.
4. Выполнить `/storm:bdd-sync` and `/storm:bdd-lint` artifact sync.
5. Запустить validator and hygiene checks.
6. Выполнить post-EXEC review-loop and report files, checks, gaps, next step.

## 14. Открытые вопросы
- Не блокирует artifact-only EXEC: актуальный product decision по attachment workflow отсутствует, поэтому default outcome этой SPEC - не covered, а blocked/unconfirmed.
- Блокирует переход к engineering delivery: является ли attachment workflow актуальной продуктовой поверхностью для пользователя?

## 15. Соответствие профилю
- Профиль: `storm-product-development`.
- Выполненные требования профиля:
  - Route явно определен как `guided-artifact-workflow`.
  - Canonical artifact path `docs/product/storm.json` сохранен.
  - Gherkin не заменяет AC и не создается без необходимости.
  - Code-only evidence не превращается в active story без confirmation.
  - `/storm:cover` не меняет tests/code без delivery-task.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-17-storm-cover-attachment-workflow-confirmation.md` | Новая SPEC | Зафиксировать scope before artifact update. |
| `docs/product/storm.json` | Планируется только после approval: conservative status/update for `CV-0007` | Canonical product artifact. |
| `docs/product/reports/coverage.md` | Планируется только после approval | Coverage report должен отражать blocked/unconfirmed state. |
| `docs/product/reports/ranking.md` | Планируется только после approval | Следующий шаг должен быть product decision or delivery SPEC, not blind test implementation. |
| `docs/product/reports/stories.md` | Планируется только после approval | Product-entry candidate/gap должен быть виден. |
| `docs/product/reports/traceability.md` | Планируется только после approval | Attachment code trace должен быть честно отмечен как unlinked or candidate. |
| `docs/product/reports/bdd-sync.md` | Планируется только после approval | Проверка отсутствия ложных scenario/test links. |
| `docs/product/reports/bdd-lint.md` | Планируется только после approval | Проверка scenario/status hygiene. |
| `features/storm/*` | Не менять по default; менять только при найденном existing product evidence или явном proposed/draft scenario need | Избежать Gherkin inflation без workflow confirmation. |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `CV-0007` | `proposed`, next uncovered item, needs confirmation | Явно blocked/unconfirmed до product decision, либо linked artifact-only при найденном existing evidence. |
| Attachment code | Product-entry candidate without story | Сохраненный candidate/internal contract evidence, не active story. |
| Minimal tests | Future ideas в backlog | Остаются future delivery hints, не создаются в artifact-only EXEC. |
| Next step | Неоднозначно: confirm workflow or implement tests | Явно: product decision first; delivery-task only after confirmation. |

## 18. Альтернативы и компромиссы
- Вариант: сразу создать active story для attachment upload/download.
- Плюсы: быстрее превращает code в product backlog.
- Минусы: выдумывает product claim без confirmation; может исказить Vision/Goal/coverage.
- Почему выбранное решение лучше в контексте этой задачи: STORM artifacts должны показывать uncertainty, а не закрывать coverage ценой ложной продуктовой связи.

- Вариант: удалить `CV-0007` из backlog.
- Плюсы: уменьшает шум.
- Минусы: теряет evidence о существующем code/API и future coverage hint.
- Почему выбранное решение лучше в контексте этой задачи: conservative blocked state сохраняет traceability and decision point.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals and Non-Goals заданы. |
| B. Качество дизайна | 6-10 | PASS | TO-BE, rules, integration points, data/rollback boundaries описаны. |
| C. Безопасность изменений | 11-13 | PASS | Tests/code запрещены; artifact-only rollout/rollback указан. |
| D. Проверяемость | 14-16 | PASS | AC, commands, planned file table and stop rules заданы. |
| E. Готовность к автономной реализации | 17-19 | PASS | Default conservative route автономен; product decision вынесен как blocker только для engineering delivery. |
| F. Соответствие профилю | 20 | PASS | STORM route, artifact chain and BDD constraints соблюдены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Artifact-only triage and stop rules clearly separated from delivery. |
| 2. Понимание текущего состояния | 5 | Указаны current reports, backlog status and конкретные attachment code entry points. |
| 3. Конкретность целевого дизайна | 5 | Conservative state, reports and validation flow описаны. |
| 4. Безопасность (миграция, откат) | 5 | Runtime untouched; rollback is artifact revert. |
| 5. Тестируемость | 5 | Validator/hygiene checks listed; tests intentionally unchanged. |
| 6. Готовность к автономной реализации | 5 | EXEC can proceed without product claim by marking blocked/unconfirmed. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-17-storm-cover-attachment-workflow-confirmation.md`, central stack (`AGENTS.md`, `routing-matrix.md`, `storm-product-development.md`, `quest-mode.md`, `spec-linter.md`, `spec-rubric.md`, `review-loops.md`), selected profile `storm-product-development`, open questions, planned changed files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: сверены current ranking/coverage/bdd-sync/traceability reports, `CV-0007` entries in `storm.json`, attachment code entry points and existing worktree status.
  - Contract pass: SPEC keeps SPEC-phase mutation limited to this file and does not authorize code/test/annotation changes.
  - Adversarial risk pass: main counterexample is silent product confirmation; mitigated by default conservative blocked/unconfirmed outcome.
  - Re-review after fixes / Fix and re-review: исправления не требовались после final review pass.
  - Stop decision: PASS; no BLOCKER/HIGH findings.
- Evidence inspected: `docs/product/reports/ranking.md`, `docs/product/reports/coverage.md`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/traceability.md`, `docs/product/storm.json` snippets for `CV-0007`, `Attachment.cs`, `AttachmentService.cs`, ServiceStack DTO, molds and mapping.
- Depth checklist:
  - Scope drift / unrelated changes: prior STORM changes are present in working tree; SPEC does not touch them.
  - Acceptance criteria: artifact-only acceptance is measurable through validator, reports and status changes.
  - Validation evidence: commands listed; no runtime tests required before approval.
  - Unsupported claims: product workflow confirmation is not assumed.
  - Regression / edge case: code/test behavior unchanged.
  - Comments/docs/changelog: no changelog needed for product artifact triage.
  - Hidden contract change: none; API and UI untouched.
  - Manual-review challenge: reviewer would likely challenge whether `Продолжай` confirms product workflow; SPEC explicitly says no.
- No-findings justification: SPEC preserves uncertainty, blocks engineering changes, and gives an autonomous conservative EXEC path.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | product-decision | Approval of this SPEC does not by itself confirm attachment workflow as active product behavior. | Treat EXEC default as blocked/unconfirmed unless user separately confirms product workflow. | accepted-risk |

- Fixed before continuing: Не применимо.
- Checks rerun: Manual SPEC linter/rubric/review-loop completed in this file.
- Needs human: SPEC approval phrase before artifact updates; separate product decision before declaring attachment workflow active/covered.
- Residual risks / follow-ups: если attachment workflow актуален, next task should be `/storm:expand` or `/storm:bdd-implement` with tests.

### Post-EXEC Review
- Статус: Не выполнен до EXEC
- Scope reviewed: Не применимо до approval.
- Decision: Не применимо до EXEC.
- Review passes:
  - Scope/Evidence pass: Не применимо.
  - Contract pass: Не применимо.
  - Adversarial risk pass: Не применимо.
  - Re-review after fixes / Fix and re-review: Не применимо.
  - Stop decision: Не применимо.
- Evidence inspected: Не применимо.
- Depth checklist:
  - Scope drift / unrelated changes: Не применимо.
  - Acceptance criteria: Не применимо.
  - Validation evidence: Не применимо.
  - Unsupported claims: Не применимо.
  - Regression / edge case: Не применимо.
  - Comments/docs/changelog: Не применимо.
  - Hidden contract change: Не применимо.
  - Manual-review challenge: Не применимо.
- No-findings justification: Не применимо до EXEC.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | execution | EXEC еще не выполнялся. | Запустить только после фразы `Спеку подтверждаю`. | pending |

- Fixed before final report: Не применимо.
- Checks rerun: Не применимо.
- Validation evidence: Не применимо.
- Unrelated changes: Не применимо.
- Needs human: approval.
- Residual risks / follow-ups: Не применимо.

## Approval
Ожидается фраза: "Спеку подтверждаю"

Важно: эта фраза утверждает artifact-only conservative EXEC. Она не подтверждает, что attachment workflow является актуальной продуктовой поверхностью. Если нужно подтвердить workflow как product behavior, это решение должно быть сказано отдельно.

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершенный значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор контекста CV-0007 | 0.86 | Product owner decision по актуальности attachment workflow | Создать conservative SPEC | Нет | Нет | Ranking/coverage называют CV-0007 следующим gap, но только после confirmation; attachment code найден без story/AC/UI evidence. | `docs/product/reports/*`, `docs/product/storm.json`, attachment code files |
| SPEC | Создание SPEC | 0.9 | Подтверждение спеки | Запросить approval | Да | Нет | На SPEC фазе разрешено менять только spec; выбран artifact-only route with stop rules. | `specs/2026-06-17-storm-cover-attachment-workflow-confirmation.md` |
| SPEC | Post-SPEC review | 0.9 | Подтверждение спеки и отдельный product decision при необходимости | Передать SPEC пользователю | Да | Нет | Review подтвердил, что SPEС не заявляет product workflow без evidence and does not authorize tests/code changes. | `specs/2026-06-17-storm-cover-attachment-workflow-confirmation.md` |
