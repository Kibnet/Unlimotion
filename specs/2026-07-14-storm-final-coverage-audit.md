# SPEC: Итоговый audit исполняемого STORM coverage 45/45

## 0. Метаданные
- Тип (профиль): artifact-only delivery-task; storm-product-development.
- Владелец: STORM /storm:cover finalization.
- Масштаб: small.
- Целевое семейство / behavior baseline: central model-behavior-baseline; implementation, tests and behaviour не меняются.
- Поверхность: Codex desktop, локальные STORM product artifacts.
- Effective runtime: не применимо.
- Eval baseline / evidence: current storm.json, six reports, central validator and PowerShell inventory of active scenarios.
- Целевой релиз / ветка: storm-bootstrap, локальный audit commit.
- Ограничения: не менять code, tests, annotations, .feature, projects, workflows, scripts, README, media or product behaviour.
- Связанные ссылки: behavior_coverage_metrics, bdd_sync, bdd_lint, traceability, coverage report.

## 1. Overview / Цель
Зафиксировать, что все active Gherkin scenarios имеют passing status, test links и step definitions, а canonical STORM metrics согласованы на 45/45.

Outcome contract:
- Success means: validator returns 0 errors; PowerShell audit returns Active=45, Passing=45, empty NotPassing/WithoutSteps/WithoutTests; reports state 45/45 consistently.
- Итоговый артефакт / output: audit section in coverage report, final process metadata in storm.json and this SPEC.
- Stop rules: остановиться и создать отдельный delivery-task, если audit reveals a missing scenario link, non-passing scenario, validator error or any need to change code/tests.

## 2. Текущее состояние (AS-IS)
- Commits 1e53bcf and ea58a36 made SC-0015-001 and SC-0015-003 executable.
- Current validator returns executable_specification_ratio 45/45 with 0 errors and 18 known duplicate-step warnings.
- Active inventory reports 45 active, 45 passing, no missing step or test links.
- Full Unlimotion.Test suite previously timed out after 304 seconds without a summary; it remains separate from scenario coverage.

## 3. Проблема
Без final audit the 45/45 claim is distributed across several artifacts and has no compact inventory evidence or explicit boundary for the historical full-suite timeout.

## 4. Цели дизайна
- Сверить canonical JSON, reports and computed inventory.
- Не расширять test or product surface.
- Явно сохранить residual limitation without weakening scenario-level 45/45 evidence.

## 5. Non-Goals (чего НЕ делаем)
- Изменение implementation, tests, annotations, CI, media or product artifacts outside final audit metadata/report.
- Повторный полный suite, media generation or remote CI invocation.
- Нормализация 18 intentional duplicate-step warnings.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- storm.json: final process audit metadata records 45/45 and known warning boundary.
- docs/product/reports/coverage.md: adds a concise final inventory section.
- this SPEC: records review and validation evidence.

### 6.2 Детальный дизайн
- Use the central validator as the schema/link and BDD-lint authority.
- Use ConvertFrom-Json inventory to count active, passing, missing step and missing test links.
- Preserve 18 warnings as intentional shared Gherkin wording; do not modify feature text to suppress them.
- Visual/video evidence: не применимо; audit changes no UI behaviour or media.
- Remote CI, generated media and full-suite PASS remain unclaimed.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Final coverage audit | Maintainer completes STORM coverage backlog | Canonical reports state 45/45 with explicit residual limitation | Validator and inventory output | All active AC/scenarios |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| 45/45 artifacts | Final audit | Metrics and reports are certified consistent | Any mismatch stops execution | No runtime interaction |
| Full-suite limitation | Final audit | Remains documented as unconfirmed | No passing claim | Separate from BDD coverage |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Coverage authority | agent | Central validator plus JSON inventory | 1.00 | None after both agree | Нет |
| Duplicate steps | agent | Retain 18 intentional warnings | 0.99 | Wording changes would alter features | Нет |
| Full suite | agent | Preserve timeout limitation; do not rerun | 1.00 | No regression PASS claim | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Scenario coverage | storm.json | Final audit metadata only | None | validator plus inventory |
| Coverage reporting | coverage.md | Append final audit section | None | report text and git diff |
| Product/test runtime | existing code/tests | None | None | No implementation diff |

## 7. Бизнес-правила / Алгоритмы
1. Active scenario is complete only if it has passing status, at least one test link and at least one step definition.
2. 45/45 is valid only when validator and computed inventory agree.
3. Full-suite timeout is not converted into a passing claim.

## 8. Точки интеграции и триггеры
- Central validate-artifacts.py reads storm.json.
- PowerShell ConvertFrom-Json reads gherkin_scenarios and behavior_coverage_metrics.
- No application or CI integration is invoked.

## 9. Изменения модели данных / состояния
- Только process audit metadata and report text.
- Persisted product and test state не меняется.

## 10. Миграция / Rollout / Rollback
- Не применимо.
- Rollback: удалить final audit metadata/report section/SPEC only.

## 11. Тестирование и критерии приёмки
1. Central validator reports 0 errors, executable ratio 45/45, reuse 181/181 and 18 known warnings.
2. Inventory reports 45 active/45 passing and empty mismatch lists.
3. git diff --check passes.
4. No implementation/test/workflow/script/media files appear in the audit diff.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| All active scenarios executable | validate-artifacts.py | PowerShell JSON inventory | validator and inventory output | - |
| Report consistency | git diff --check | coverage report review | coverage.md | - |
| Runtime non-regression boundary | No rerun by design | Existing historical timeout preserved | storm.json | Full suite has no summary |

## 12. Риски и edge cases
- Validator could pass while report prose is stale; mitigate by updating only after inventory confirmation.
- Full-suite status could be overclaimed; mitigate with explicit unconfirmed wording.
- No code changes are permitted; any mismatch stops this audit.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| 45/45 might hide full-suite failure | Scenario metrics and suite gate differ | Report both boundaries separately | mitigated |
| Final audit might alter coverage semantics | Metadata can drift from evidence | Validator and computed inventory are mandatory | mitigated |

### Rework Prevention Checklist
- Final result is user-observable in canonical reports.
- Every claim has validator or inventory evidence.
- No unowned decision remains.
- Role-based review is completed below.

## 13. План выполнения
1. Capture clean Git state, validator and JSON inventory evidence.
2. Add final audit metadata/report only.
3. Re-run validator, inventory and diff checks.
4. Post-EXEC review and isolated commit.

## 14. Открытые вопросы
Нет.

## 15. Соответствие профилю
- Профиль: storm-product-development.
- Выполненные требования: canonical artifacts remain Russian; no acceptance criteria replacement; no implementation scope drift; executable BDD metrics are auditable.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| specs/2026-07-14-storm-final-coverage-audit.md | New audit SPEC | Сохраняет approval/review evidence |
| docs/product/storm.json | Final audit metadata | Canonical completion state |
| docs/product/reports/coverage.md | Final audit section | Readable coverage conclusion |
| docs/product/reports/ranking.md | Final ranking conclusion | Closes the executable BDD backlog |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Coverage result | 45/45 in metrics and reports | 45/45 independently audited |
| Full-suite status | historical timeout | unchanged, explicitly not PASS |
| Product/test surface | unchanged | unchanged |

## 18. Альтернативы и компромиссы
- Вариант: rerun full suite or remote CI.
- Плюсы: broader regression evidence.
- Минусы: historic timeout/network/external execution and no necessity for this artifact audit.
- Почему выбранное решение лучше: validates the stated STORM completion condition without side effects or unsupported claims.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Scope and stop rules explicit |
| B. Качество дизайна | 6-10 | PASS | Validator/inventory/report responsibilities explicit |
| C. Безопасность изменений | 11-13 | PASS | Artifact-only boundary |
| D. Проверяемость | 14-16 | PASS | Exact computed gates named |
| E. Готовность к автономной реализации | 17-19 | PASS | No open decision |
| F. Соответствие профилю | 20 | PASS | STORM evidence rules retained |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| Ясность цели и границ | 5 | One artifact-only audit |
| Понимание текущего состояния | 5 | Current metrics and limitation inspected |
| Конкретность целевого дизайна | 5 | Exact sources and checks named |
| Безопасность | 5 | No runtime/config mutation |
| Тестируемость | 5 | Validator and inventory agree |
| Готовность к автономной реализации | 5 | No human decision remains |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does final report distinguish scenario coverage from full-suite status? | PASS | Both boundaries explicit |
| UX / designer | not applicable | No UI/output behaviour change | PASS | None |
| Tester / validation | applicable | Do validator and inventory independently establish 45/45? | PASS | Both are mandatory |
| Developer / architect | applicable | Is scope limited to canonical artifacts? | PASS | No code diff permitted |
| Delivery / operations / security | applicable | Are CI/network/media side effects excluded? | PASS | No external execution |

### Post-SPEC Review
- Статус: PASS.
- Scope reviewed: this SPEC, central stack, current 45/45 validator output, inventory output, clean Git state and planned files.
- Decision: active user auto-approval permits EXEC.
- Evidence inspected: validator 0 errors/18 warnings/45-45; inventory Active=45, Passing=45 and empty mismatch lists; coverage report and historical full-suite note.
- Adversarial finding fixed before EXEC: final result must not imply a green full suite; the report and metadata keep timeout explicitly unconfirmed.
- Needs human: нет.
- Residual risk: full-suite regression evidence remains unavailable by design.

### Post-EXEC Review
- Статус: PASS.
- Scope reviewed: approved artifact-only SPEC, storm.json, coverage/ranking reports, computed inventory, validator output and Git diff; no application/test surface.
- Findings: нет. Validator and independently computed inventory agree on 45 active/45 passing, with no missing test links or step definitions.
- Evidence: validate-artifacts.py passed with 0 errors, executable ratio 45/45, reuse 181/181 and 18 known warnings; inventory has empty mismatch lists; git diff --check passed.
- Residual risk: historical full-suite timeout remains unconfirmed and is explicitly excluded from the scenario-coverage PASS claim.
- Decision: no corrective change is needed; final audit is ready for its isolated commit.

## Approval
Active workflow auto-approval after PASS review; canonical phrase: Спеку подтверждаю.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Select final artifact-only audit | 1.00 | Нет | EXEC | Нет | User auto-approval already active | Validator and inventory already agree on 45/45 | This SPEC |
| EXEC | Validate and record final coverage audit | 1.00 | Нет | Commit and complete goal | Нет | User auto-approval already active | Independent inventory and central validator agree without implementation changes | storm.json, coverage/ranking reports, audit SPEC |
