# STORM Product Decision: Attachment Workflow

## 0. Метаданные
- Тип (профиль): `guided-artifact-workflow` + `storm-product-development`; QUEST SPEC gate для product decision, которое может повлиять на будущие stories/tests/code.
- Владелец: Product owner / Codex после явного выбора варианта.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая ветка `storm-bootstrap`.
- Ограничения: на фазе SPEC менять только этот файл; до отдельного выбора не менять `docs/product/*`, code, tests, test annotations, feature files, API behavior.
- Связанные ссылки: `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md`, `docs/product/reports/stories.md`, `specs/2026-06-17-storm-cover-attachment-workflow-confirmation.md`.

## 1. Overview / Цель
Принять явное продуктовое решение по `CV-0007`: считать ли attachment workflow актуальной пользовательской поверхностью Unlimotion или оставить найденный attachment backend/API code как internal/orphan contract candidate.

Outcome contract:
- Success means: выбран один из вариантов решения, и дальнейший STORM route становится однозначным.
- Итоговый артефакт / output: после approval Codex обновит только соответствующие product artifacts или остановится с отдельной delivery SPEC, если выбран путь с tests/code.
- Stop rules: если выбран вариант, требующий tests/code/API/UI behavior changes, не начинать реализацию в этой SPEC; подготовить отдельную `/storm:expand` или `/storm:bdd-implement` SPEC.

## 2. Текущее состояние (AS-IS)
- `CV-0007` закоммичен как `blocked_pending_product_decision` в `bb861ba docs(storm): block attachment workflow pending product decision`.
- Evidence:
  - `src/Unlimotion.Domain/Attachment.cs` содержит attachment metadata.
  - `src/Unlimotion.Server.ServiceModel/Attachment.cs` содержит ServiceStack routes for upload/download.
  - `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs` сохраняет файл и metadata behind authenticated endpoints.
  - `src/Unlimotion.Server/AppModelMapping.cs` маппит attachment domain в API/hub molds.
- Missing evidence:
  - нет active story;
  - нет acceptance criteria;
  - нет Gherkin scenario;
  - нет UI/docs workflow;
  - нет linked tests для user-facing attachment behavior.

## 3. Проблема
Одна корневая проблема: без product decision STORM не может решить, является ли attachment code будущей продуктовой возможностью, внутренним/осиротевшим contract candidate или кандидатом на cleanup.

## 4. Цели дизайна
- Разделение ответственности: product decision отдельно от engineering delivery.
- Повторное использование: сохранить найденное evidence для будущего route.
- Тестируемость: не запускать `/storm:bdd-implement` без подтвержденного behavior.
- Консистентность: не создавать ложные stories/scenarios.
- Обратная совместимость: не менять runtime.

## 5. Non-Goals (чего НЕ делаем)
- Не реализуем attachment upload/download.
- Не добавляем tests.
- Не меняем API, ServiceStack DTO, RavenDB model, UI или storage.
- Не удаляем attachment code.
- Не создаем active story без явного выбора варианта A.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- Product owner -> выбирает вариант A, B или C.
- Codex -> после выбора обновляет только допустимые artifacts или готовит следующую delivery SPEC.
- `docs/product/storm.json` -> canonical reflection of decision.
- `docs/product/reports/*` -> human-readable reflection of decision and next route.

### 6.2 Детальный дизайн
Варианты решения:

| Вариант | Решение | Что делает Codex после approval | Когда выбирать |
| --- | --- | --- | --- |
| A | Attachment workflow актуален как продуктовая поверхность | Подготовить отдельную `/storm:expand` SPEC или artifact update для proposed story/AC/Gherkin; tests/code только через следующую `/storm:bdd-implement` SPEC | Если пользователю реально нужен workflow загрузки/скачивания файлов |
| B | Attachment code остается internal/orphan contract candidate | Artifact-only update: зафиксировать decision, убрать `CV-0007` из active `/storm:cover` очереди, оставить evidence для future revisit | Если сейчас нет пользовательского attachment workflow |
| C | Attachment code потенциально dead/deprecated | Не удалять код здесь; подготовить отдельную `/storm:cleanup` delivery SPEC with traceability proof | Если capability точно не нужна и должна быть удалена |

Recommended default: B.

Reason: current evidence confirms code/API, but not product workflow. B preserves traceability without inventing requirements or deleting code.

Visual planning artifact для UI-facing изменений: Не применимо; эта SPEC принимает product decision, UI не проектирует.

UI test video evidence: Не применимо.

## 7. Бизнес-правила / Алгоритмы (если есть)
- `Спеку подтверждаю` без выбора A/B/C не должно считаться product decision.
- Вариант A открывает product modeling work, но не разрешает code/test changes.
- Вариант B закрывает текущий `/storm:cover` loop for `CV-0007` as consciously deferred/internal candidate.
- Вариант C требует `/storm:cleanup` and proof that no active/implemented/proposed story, constraint or enabler depends on the code.

## 8. Точки интеграции и триггеры
- Trigger: user approves this SPEC and names option A, B or C.
- After A: create/confirm product story through `/storm:expand`; then separate `/storm:bdd-implement`.
- After B: update `storm.json` and reports artifact-only.
- After C: prepare cleanup SPEC; no deletion in this step.

## 9. Изменения модели данных / состояния
- Runtime model: no changes.
- Product artifact model:
  - A may create proposed story/AC/scenarios in a later artifact task.
  - B updates status/decision fields only.
  - C creates cleanup candidate only through a later delivery SPEC.

## 10. Миграция / Rollout / Rollback
- Rollout: artifact-only decision sync.
- Rollback: revert decision artifact commit.
- Runtime rollback: not applicable.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. SPEC records all viable product decision options.
  2. Recommended default does not invent product behavior.
  3. Approval requires explicit option A/B/C.
  4. No code/tests/test annotations are changed by this SPEC phase.
  5. If executed for option B, STORM validator must pass.
- Какие тесты добавить/изменить: none in this SPEC.
- Characterization tests / contract checks: not applicable.
- Visual acceptance: not applicable.
- UI video evidence: not applicable.
- Commands for option B artifact sync:
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - `rg -n "[ \t]+$" docs/product features/storm specs/2026-06-18-storm-attachment-workflow-product-decision.md`
- Stop rules:
  - If user chooses A, do not create tests/code in this SPEC.
  - If user chooses C, do not delete code in this SPEC.
  - If no option is chosen, stop and ask for product decision.

## 12. Риски и edge cases
- Риск: вариант B может выглядеть как отказ от capability. Смягчение: формулировать as internal/orphan candidate, not removed/deprecated.
- Риск: вариант A создаст backlog without real UX scope. Смягчение: next `/storm:expand` must define workflow and observable behavior before tests/code.
- Риск: вариант C опасен без traceability proof. Смягчение: deletion only through `/storm:cleanup`.

## 13. План выполнения
1. Получить явный выбор A/B/C.
2. Если B выбран: обновить `storm.json` and reports artifact-only, run validator/hygiene checks, commit if requested.
3. Если A выбран: подготовить next SPEC for `/storm:expand CV-0007 attachment workflow` without code/tests.
4. Если C выбран: подготовить next SPEC for `/storm:cleanup attachment code candidate` without deletion until approved.

## 14. Открытые вопросы
- Решено 2026-06-18: product owner выбрал Вариант B.

## 15. Соответствие профилю
- Профиль: `storm-product-development`.
- Выполненные требования профиля:
  - Route explicitly classified as guided artifact/product decision workflow.
  - Code-only evidence is not promoted to active story.
  - Tests/code/test annotations remain gated by separate delivery-task.
  - Uncertainty remains visible in artifacts and final response.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-18-storm-attachment-workflow-product-decision.md` | Новая decision SPEC | Зафиксировать product decision gate for `CV-0007`. |
| `docs/product/storm.json` | Только после option B approval: decision/status sync | Canonical artifact. |
| `docs/product/reports/*` | Только после option B approval: report sync | Human-readable decision trace. |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| CV-0007 | blocked pending product decision | Awaiting explicit option A/B/C |
| Attachment code | backend/API evidence without product workflow | Decision branch: product workflow / internal candidate / cleanup candidate |
| Next route | ambiguous after blocked state | Route determined by option |

## 18. Альтернативы и компромиссы
- Вариант A:
  - Плюсы: превращает hidden capability в explicit product backlog.
  - Минусы: требует UX/API/test definition and future delivery work.
- Вариант B:
  - Плюсы: safest, preserves evidence and avoids false coverage.
  - Минусы: does not improve user-facing product behavior now.
- Вариант C:
  - Плюсы: может уменьшить dead-code surface.
  - Минусы: highest risk; cleanup needs deeper proof.

Почему выбранный default лучше: B is conservative and reversible; it matches current evidence.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals and Non-Goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Decision options, routes and rollback described. |
| C. Безопасность изменений | 11-13 | PASS | Code/tests prohibited; deletion gated by cleanup SPEC. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria and validation commands listed for artifact route. |
| E. Готовность к автономной реализации | 17-19 | PARTIAL | Requires human option A/B/C before EXEC. |
| F. Соответствие профилю | 20 | PASS | STORM uncertainty and delivery gates preserved. |

Итог: ГОТОВО ДЛЯ PRODUCT DECISION

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Decision scope is explicit. |
| 2. Понимание текущего состояния | 5 | Current committed blocked state and evidence are listed. |
| 3. Конкретность целевого дизайна | 5 | Options map to concrete next routes. |
| 4. Безопасность (миграция, откат) | 5 | Runtime untouched; cleanup gated separately. |
| 5. Тестируемость | 5 | Artifact route has validator/hygiene checks; delivery routes are gated. |
| 6. Готовность к автономной реализации | 2 | Cannot execute without product decision. |

Итоговый балл: 27 / 30
Зона: готово к автономному выполнению после выбора варианта

### Post-SPEC Review
- Статус: ASK-HUMAN
- Scope reviewed: this SPEC, current ranking/coverage reports, committed `CV-0007` state, selected profile `storm-product-development`, planned files.
- Decision: нужен выбор пользователя before EXEC.
- Review passes:
  - Scope/Evidence pass: reviewed clean worktree after `bb861ba`, ranking says product decision is required.
  - Contract pass: SPEC does not allow implicit product confirmation or code/test changes.
  - Adversarial risk pass: no single option can be chosen by the agent because it changes product scope.
  - Re-review after fixes / Fix and re-review: no fixes required.
  - Stop decision: ASK-HUMAN.
- Evidence inspected: `docs/product/reports/ranking.md`, `docs/product/reports/coverage.md`, git status clean, recent commits.
- Depth checklist:
  - Scope drift / unrelated changes: none.
  - Acceptance criteria: decision options and stop rules are explicit.
  - Validation evidence: not applicable until EXEC.
  - Unsupported claims: none; no workflow is confirmed.
  - Regression / edge case: runtime untouched.
  - Comments/docs/changelog: not applicable.
  - Hidden contract change: none.
  - Manual-review challenge: reviewer would ask whether Codex should choose B automatically; rejected because product scope requires owner decision.
- No-findings justification: The only blocker is intentional product choice, not spec quality.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | product-decision | No product owner choice between A/B/C. | User must choose an option. | ask-human |

- Fixed before continuing: Not applicable.
- Checks rerun: Manual SPEC review.
- Needs human: choose A, B or C.
- Residual risks / follow-ups: Future delivery depends on selected route.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec with Вариант B, `docs/product/storm.json`, reports in `docs/product/reports/*`, `git status --short`, `git diff --stat`, validation commands.
- Decision: можно завершать.
- Review passes:
  - Scope/Evidence pass: changes are limited to product artifacts and this SPEC; code, tests, test annotations and feature files are unchanged.
  - Contract pass: Вариант B is reflected as `internal_orphan_contract_candidate`; no active story/scenario/test links were created for `CV-0007`.
  - Adversarial risk pass: main risk is making `CV-0007` look covered; reports explicitly state it is not coverage and not active product behavior.
  - Re-review after fixes / Fix and re-review: final validation evidence was inspected after artifact sync.
  - Stop decision: PASS; no BLOCKER/HIGH findings.
- Evidence inspected: updated `storm.json`, coverage/ranking/stories/bdd-sync/bdd-lint/traceability reports, validator output, diff hygiene output.
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated files changed.
  - Acceptance criteria: Вариант B selected, artifacts updated, no code/tests changed, validator planned/recorded.
  - Validation evidence: validator and hygiene checks executed after updates.
  - Unsupported claims: no claim that attachment workflow is covered or supported.
  - Regression / edge case: runtime unchanged.
  - Comments/docs/changelog: changelog not applicable for product artifact sync.
  - Hidden contract change: none.
  - Manual-review challenge: reviewer may ask why `CV-0007` remains in backlog table; answer: retained for traceability as non-active internal/orphan candidate, not active cover item.
- No-findings justification: artifacts now align with explicit Вариант B and preserve traceability without product inflation.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | follow-up | `SC-0014-002` remains the remaining draft behavior gap. | Open separate `/storm:bdd-implement ST-0014` SPEC only if TelegramBot timer behavior is supported. | follow-up |

- Fixed before final report: Product decision state and report wording aligned to `internal_orphan_contract_candidate`.
- Checks rerun: STORM validator, `git diff --check`, trailing whitespace scan.
- Validation evidence: STORM validator OK 0 errors / 0 warnings; `git diff --check` passed with LF/CRLF warnings only; trailing whitespace scan had no matches.
- Unrelated changes: none.
- Needs human: none for Вариант B execution; future ST-0014 behavior still needs separate approval.
- Residual risks / follow-ups: future attachment workflow revisit requires new product decision.

## Approval
Ожидается одна из фраз:
- `Спеку подтверждаю. Вариант A`
- `Спеку подтверждаю. Вариант B`
- `Спеку подтверждаю. Вариант C`

`Спеку подтверждаю` без варианта не запускает EXEC, потому что product decision остается невыбранным.

Статус: подтверждено пользователем 2026-06-18 фразой `Спеку подтверждаю. Вариант B`.

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Product decision routing | 0.88 | Выбор A/B/C | Запросить product decision | Да | Нет | Ranking после `CV-0007` не допускает дальнейший `/storm:cover` без решения владельца продукта. | `specs/2026-06-18-storm-attachment-workflow-product-decision.md` |
| SPEC | Post-SPEC review | 0.9 | Выбор A/B/C | Передать SPEC пользователю | Да | Нет | Review показал, что агент не должен выбирать product scope самостоятельно. | `specs/2026-06-18-storm-attachment-workflow-product-decision.md` |
| EXEC | Вариант B artifact sync | 0.92 | Нет | Запустить validator и hygiene checks | Нет | Да, пользователь выбрал Вариант B | `CV-0007` снят с active `/storm:cover` queue and retained as internal/orphan contract candidate. | `docs/product/storm.json`, `docs/product/reports/*` |
| EXEC | Post-EXEC review | 0.91 | Нет | Передать итог пользователю | Нет | Нет | Scope соблюден: code/tests/test annotations untouched, product artifacts consistent with Вариант B. | `specs/2026-06-18-storm-attachment-workflow-product-decision.md`, `docs/product/*` |
