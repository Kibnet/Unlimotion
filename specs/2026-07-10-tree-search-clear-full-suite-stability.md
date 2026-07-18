# Full-suite stability: TreeSearch_ClearSearch_RestoresExpansionState teardown

## 0. Метаданные
- Тип (профиль): delivery-task, validation-stability, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `storm-bootstrap`
- Ограничения: не менять product behavior, production code, UI selectors/layout, STORM acceptance criteria, Gherkin wording или test annotations
- Связанные ссылки: `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `src/Unlimotion.Test/HeadlessSessionExtensions.cs`, full-suite logs in `C:\tmp\unlimotion-full-suite-sc0003-unlocked-time-bdd*.log`

## 1. Overview / Цель
Восстановить full-suite validation gate для текущего `/storm:cover` slice. Полный прогон дважды упал в unrelated UI test `TreeSearch_ClearSearch_RestoresExpansionState(CompletedTree)`, хотя `SC-0003-002` BDD test и `TaskAvailabilityCalculationTests` проходят.

Outcome contract:
- Success means: targeted rerun падающего UI test проходит, full `Unlimotion.Test` проходит, BDD/artifact changes по `SC-0003-002` остаются без product-code изменений.
- Итоговый артефакт / output: узкое test-only stability изменение + обновленная validation evidence.
- Stop rules: не маскировать real assertion failure; остановиться, если нужен product code/UI behavior change.

## 2. Текущее состояние (AS-IS)
- Full run `C:\tmp\unlimotion-full-suite-sc0003-unlocked-time-bdd.log`: 573/574, failure в `TreeSearch_ClearSearch_RestoresExpansionState(CompletedTree)` из-за timeout в setup.
- Isolated rerun: 7/7 passed.
- Full retry `C:\tmp\unlimotion-full-suite-sc0003-unlocked-time-bdd-retry.log`: 573/574, тот же параметр, failure в `Avalonia.Headless.HeadlessUnitTestSession.DisposeAsync()` с `NullReferenceException`.
- В `HeadlessSessionExtensions` уже есть helper `DisposeIgnoringHeadlessTeardownNullReferenceAsync`, применяемый в этом же test class.
- Prior evidence в `specs/2026-06-02-search-selection-restore.md` фиксирует этот же test как order-sensitive full-suite blocker, который проходит targeted.

## 3. Проблема
Full-suite gate блокируется known order-sensitive Avalonia.Headless teardown/setup нестабильностью unrelated UI test. Без стабилизации текущий BDD-slice не может получить чистый full-suite evidence.

## 4. Цели дизайна
- Сохранить assertions и параметры теста.
- Использовать существующий teardown helper.
- Ограничить изменение одним методом `TreeSearch_ClearSearch_RestoresExpansionState`.
- Не менять product behavior и STORM wording.

## 5. Non-Goals
- Не менять `TaskTreeManager`, `UnifiedTaskStorage`, `MainWindowViewModel` или production UI.
- Не skip-ать `CompletedTree` parameter.
- Не менять assertions сценария поиска/clear search.
- Не чинить все headless teardown usages в репозитории.

## 6. Предлагаемое решение (TO-BE)
Заменить `await using var session = HeadlessUnitTestSession.StartNew(typeof(App));` в `TreeSearch_ClearSearch_RestoresExpansionState` на existing pattern: `var session`, `try { await session.DispatchAsync(...); } finally { await session.DisposeIgnoringHeadlessTeardownNullReferenceAsync(); }`.

## 7. Бизнес-правила / Алгоритмы
Не применимо: изменение только в test harness teardown.

## 8. Точки интеграции и триггеры
- `MainControlTreeCommandsUiTests.TreeSearch_ClearSearch_RestoresExpansionState`.
- `HeadlessSessionExtensions.DisposeIgnoringHeadlessTeardownNullReferenceAsync`.

## 9. Изменения модели данных / состояния
Не применимо.

## 10. Миграция / Rollout / Rollback
Rollback: вернуть `await using var session` в одном test method.

## 11. Тестирование и критерии приёмки
- `TreeSearch_ClearSearch_RestoresExpansionState` targeted проходит 7/7.
- `StormTaskAvailabilityUnlockedTimeExecutableSpecTests` остается passing 1/1.
- Full `Unlimotion.Test` проходит 574/574 либо новый unrelated blocker классифицирован отдельно.
- Команды: targeted UI, targeted BDD, `dotnet build`, full `dotnet test` with `--output Detailed`.

## 12. Риски и edge cases
- Helper может скрыть real dispose regression. Риск ограничен: helper ловит только `NullReferenceException` со stack frame `Avalonia.Headless.HeadlessUnitTestSession.DisposeAsync`.
- Initial timeout может повториться отдельно от dispose NRE. Это проверяет full retry после изменения.

## 13. План выполнения
1. Применить existing helper к одному test method.
2. Запустить targeted UI method и новый BDD test.
3. Запустить full suite.
4. Обновить validation evidence в STORM reports/specs.

## 14. Открытые вопросы
Нет блокирующих.

## 15. Соответствие профилю
- Профиль: `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля: UI test остается UI-level, assertions не удаляются, используется existing Avalonia.Headless pattern, full run обязателен.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` | Один метод переводится на explicit dispose helper | Стабилизировать known headless teardown NRE |
| `specs/2026-07-10-tree-search-clear-full-suite-stability.md` | Новая SPEC | QUEST audit trail |
| `docs/product/reports/*`, `docs/product/storm.json` | Только validation evidence после full suite | Синхронизация STORM gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Test assertions | Проверяют clear search expansion state | Без изменений |
| Headless teardown | Raw `await using` вызывает `DisposeAsync()` | Existing helper игнорирует только known Avalonia.Headless teardown NRE |
| Product behavior | Без изменений | Без изменений |

## 18. Альтернативы и компромиссы
- Rerun full suite до passing: gate остается недетерминированным.
- Skip `CompletedTree`: теряется coverage. Отклонено.
- Выбранный вариант: existing teardown helper для одного known flaky method.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Existing helper, один метод |
| C. Безопасность изменений | 11-13 | PASS | Product behavior/schema/UI не меняются |
| D. Проверяемость | 14-16 | PASS | Targeted UI, BDD и full suite заданы |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет блокирующих вопросов |
| F. Соответствие профилю | 20 | PASS | UI test coverage сохраняется |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один known full-suite blocker |
| 2. Понимание текущего состояния | 5 | Два full logs и targeted pass учтены |
| 3. Конкретность целевого дизайна | 5 | Exact helper и метод указаны |
| 4. Безопасность (миграция, откат) | 5 | Test-only rollback локальный |
| 5. Тестируемость | 5 | Targeted UI + full suite |
| 6. Готовность к автономной реализации | 5 | Нет open questions |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `MainControlTreeCommandsUiTests`, `HeadlessSessionExtensions`, two full-suite logs, isolated rerun log, prior spec evidence.
- Decision: можно выполнять; пользователь подтвердил SPEC workflow ранее, active goal требует довести validation gate.
- Review passes: Scope/Evidence PASS; Contract PASS; Adversarial risk PASS; Stop decision PASS.
- No-findings justification: изменение повторяет уже принятый pattern в том же class, assertions не меняет.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Initial timeout path может повториться отдельно от dispose NRE | Подтвердить targeted UI и full suite после изменения | accepted-risk |

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved SPEC, `MainControlTreeCommandsUiTests` diff, targeted UI output, BDD output, full-suite output.
- Decision: можно считать full-suite blocker закрытым.
- Review passes: Scope/Evidence PASS; Contract PASS; Adversarial risk PASS; Stop decision PASS.
- Evidence inspected:
  - `TreeSearch_ClearSearch_RestoresExpansionState*` targeted passed 7/7 after patch.
  - `StormTaskAvailabilityUnlockedTimeExecutableSpecTests` passed 1/1 after patch.
  - Full `Unlimotion.Test` passed 574/574 after patch.
- No-findings justification: test body assertions preserved; change only routes known Avalonia.Headless teardown NRE through existing helper.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Initial setup timeout was separate from dispose NRE | Full suite passed after helper patch, no additional change needed | fixed |

## Approval
Подтверждено текущим workflow пользователя: SPEC auto-approved for execution.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | full-suite stability | 0.89 | Нет | Перейти к EXEC | Нет | Да, workflow auto approval | Existing helper уже используется в этом class, assertions остаются | `specs/2026-07-10-tree-search-clear-full-suite-stability.md` |
| EXEC | full-suite stability | 0.92 | Нет | Commit после финальных checks | Нет | Да, workflow auto approval | Existing helper applied to one method; targeted UI and full suite passed | `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`; `specs/2026-07-10-tree-search-clear-full-suite-stability.md` |
