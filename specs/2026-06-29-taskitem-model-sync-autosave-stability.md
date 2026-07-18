# TaskItem model-sync autosave stability

## 0. Метаданные
- Тип (профиль): delivery-task, full-suite stability fix, `testing-dotnet`, `dotnet-desktop-client`, `storm-product-development`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая ветка `storm-bootstrap`
- Ограничения: не менять пользовательское поведение outline paste; не менять test annotations; не менять Gherkin wording; исправить только full-suite stability blocker
- Связанные ссылки: `SC-0002-001`, `TS-0039`, `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`, `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask`

## 1. Overview / Цель
Закрыть full-suite blocker, найденный после `/storm:bdd-implement SC-0002-001`: в полном прогоне падают outline paste tests из-за фонового autosave timeout при model-sync обновлении `TaskItemViewModel`.

Outcome contract:
- Success means: `TaskItemViewModel.Update(TaskItem)` не запускает autosave при синхронизации полей из storage/cache, targeted paste-outline tests проходят, полный `Unlimotion.Test` проходит.
- Итоговый артефакт / output: production stability fix + targeted/full validation + синхронизация STORM reports.
- Stop rules: остановиться, если требуется менять публичную модель статусов, outline paste UX, persisted schema или acceptance criteria.

## 2. Текущее состояние (AS-IS)
- `TaskItemViewModel.CanAutosave` уже проверяет `IsInitialized && !_isUpdatingFromModel`.
- В `TaskItemViewModel.Update(TaskItem)` scalar-поля (`Title`, `Description`, `Status`, dates, etc.) обновляются до включения `_isUpdatingFromModel`.
- Это означает, что property subscriptions воспринимают model-sync как пользовательское изменение и могут запускать `SaveItemCommand`.
- В full-suite порядке проявляется timeout в `TaskTreeManager.UpdateTask`, после чего outline relation assertions не успевают подтвердиться.
- Изолированно `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask` прошёл, но два полных прогона упали на related outline paste checks.

## 3. Проблема
Guard от autosave при model-sync включён слишком поздно и не покрывает scalar-property updates. Из-за этого storage/cache update может породить лишний nested update и race с relation propagation.

## 4. Цели дизайна
- Минимальный production fix: охватить весь `TaskItemViewModel.Update` флагом `_isUpdatingFromModel`.
- Сохранить ручной autosave для пользовательских изменений после завершения sync.
- Не менять outline paste алгоритм и тестовые ожидания.
- Подтвердить fix targeted tests и full suite.

## 5. Non-Goals
- Не переписывать `TaskTreeManager`.
- Не менять throttling timings.
- Не менять `CreateTaskFromOutlineNode`, если root cause закрывается model-sync guard.
- Не добавлять новые продуктовые stories/scenarios.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.ViewModel/TaskItemViewModel.cs` -> guard `_isUpdatingFromModel` покрывает весь `Update(TaskItem)`.
- `src/Unlimotion.Test/*` -> существующие paste-outline tests остаются regression evidence; новых annotations не требуется.
- `docs/product/reports/*` и `storm.json` -> зафиксировать full-suite stability evidence для текущего BDD slice.

### 6.2 Детальный дизайн
- В начале `Update(TaskItem)` после проверки `Id` установить `_isUpdatingFromModel = true`.
- Все scalar fields, collections, nested repeater model sync и version sync выполнить внутри `try`.
- В `finally` вернуть `_isUpdatingFromModel = false` и вызвать `RegisterCompletionCriteriaPropertyChangedSubscription()`.
- Сохранить текущую логику сравнения/присваивания полей.
- Visual planning artifact: не применимо, UI поведение не меняется.
- UI video evidence: fallback; используется targeted Avalonia.Headless/TUnit evidence без видео.

## 7. Бизнес-правила / Алгоритмы
- Storage/cache model-sync не является пользовательским редактированием и не должен запускать autosave.
- Пользовательские изменения после sync должны по-прежнему запускать autosave через существующие subscriptions.

## 8. Точки интеграции и триггеры
- `TaskItemViewModel.Update(TaskItem)` вызывается при обновлении item из storage/cache.
- `CanAutosave` уже используется в status/property/repeater/completion criteria subscriptions.

## 9. Изменения модели данных / состояния
- Persisted schema не меняется.
- Runtime state: `_isUpdatingFromModel` активен дольше, только на время sync одного model update.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется.
- Rollback: вернуть прежнюю область `_isUpdatingFromModel`, если targeted tests или full suite покажут regression.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `TaskItemViewModel.Update` не вызывает autosave во время scalar sync.
  - `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask` проходит.
  - `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` проходит.
  - `StormTaskStatusSupportExecutableSpecTests` остаётся passing.
  - Full `Unlimotion.Test` проходит.
- Команды проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal /nr:false`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/PasteTaskOutline_CreatesNestedTasksUnderCurrentTask" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskStatusSupportExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`

## 12. Риски и edge cases
- Риск: слишком широкий guard может подавить legitimate autosave. Смягчение: guard действует только внутри `Update(TaskItem)`, то есть при внешнем model-sync, а не при пользовательских действиях.
- Риск: completion criteria subscription останется stale. Смягчение: `RegisterCompletionCriteriaPropertyChangedSubscription()` остаётся в `finally`.
- Риск: full-suite failure имеет второй источник. Смягчение: targeted и full evidence обязательны; при новом источнике остановиться на новом stability scope.

## 13. План выполнения
1. Перенести `_isUpdatingFromModel` guard на весь `Update(TaskItem)`.
2. Запустить targeted paste-outline tests и новый STORM BDD test.
3. Обновить STORM reports с final full-suite evidence.
4. Запустить validator, diff checks и полный suite.
5. Выполнить post-EXEC review.

## 14. Открытые вопросы
Нет.

## 15. Соответствие профилю
- Профиль: `storm-product-development` + `dotnet-desktop-client`
- Выполненные требования профиля: blocker найден через `/storm:cover` full-suite gate; fix минимален; product artifacts остаются на русском; UI behavior покрывается existing UI tests.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | Расширить `_isUpdatingFromModel` на весь `Update(TaskItem)` | Убрать autosave при model-sync |
| `docs/product/storm.json` | Обновить validation evidence/full-suite status | Синхронизация STORM |
| `docs/product/reports/coverage.md` | Обновить full-suite gate | `/storm:cover` evidence |
| `docs/product/reports/bdd-sync.md` | Обновить sync evidence | `/storm:bdd-sync` |
| `docs/product/reports/bdd-lint.md` | Обновить lint evidence | `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Model sync | scalar updates могли запускать autosave | весь sync защищён `_isUpdatingFromModel` |
| Full suite | 569/570, outline paste failure | ожидается 570/570 |
| Product behavior | Outline paste создаёт дерево | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: увеличить wait timeout в тестах. Отклонено: маскирует root cause autosave timeout.
- Вариант: изменить paste algorithm. Отклонено: root cause в model-sync guard, а не в UX.
- Выбранный вариант: расширить уже существующий guard, потому что он ровно выражает контракт "sync is not user edit".

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Root cause, scope и non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Изменение локальное и связано с существующим guard |
| C. Безопасность изменений | 11-13 | PASS | Schema/UX не меняются; rollback простой |
| D. Проверяемость | 14-16 | PASS | Targeted и full commands указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | Блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | Full-suite blocker закрывается через delivery-task |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один stability blocker |
| 2. Понимание текущего состояния | 5 | Логи и root cause связаны с кодом |
| 3. Конкретность целевого дизайна | 5 | Точный метод и guard |
| 4. Безопасность (миграция, откат) | 5 | Нет schema/UX migration |
| 5. Тестируемость | 5 | Targeted + full suite |
| 6. Готовность к автономной реализации | 5 | Нет открытых решений |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: full-suite logs `C:\tmp\unlimotion-full-suite-sc0002-status-support-bdd*.log`, `TaskItemViewModel.Update`, paste-outline tests, approved STORM BDD scope
- Decision: можно выполнять как required stability sub-scope for full-suite gate
- Review passes:
  - Scope/Evidence pass: проверены два full-suite failure logs, isolated passing paste test и код guard.
  - Contract pass: fix не меняет UX/AC/Gherkin и соответствует full-suite requirement.
  - Adversarial risk pass: timeout нельзя закрывать только retry; выбран root-cause guard.
  - Re-review after fixes / Fix and re-review: до EXEC не требуется.
  - Stop decision: PASS.
- Evidence inspected: failure lines for `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`, `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask`, `TaskItemViewModel.Update`, `CanAutosave`.
- Depth checklist:
  - Scope drift / unrelated changes: ограничено model-sync autosave.
  - Acceptance criteria: full-suite blocker связан с existing outline paste behavior.
  - Validation evidence: commands listed.
  - Unsupported claims: no UX/schema claims.
  - Regression / edge case: legitimate user autosave remains outside `Update(TaskItem)`.
  - Comments/docs/changelog: changelog не требуется.
  - Hidden contract change: only prevents autosave during model-sync.
  - Manual-review challenge: reviewer проверил бы, что `finally` восстанавливает subscriptions and flag.
- No-findings justification: root cause maps to existing guard contract; alternative timeout-only fix rejected.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | UI video evidence отсутствует | Использовать targeted headless tests как fallback | accepted-risk |

- Fixed before continuing: не требуется
- Checks rerun: ручная SPEC linter/rubric
- Needs human: нет
- Residual risks / follow-ups: если full suite выявит новый unrelated blocker, создать отдельную SPEC

### Post-EXEC Review
- Статус: PASS
- Scope executed: full-suite stability blocker for `TaskItemViewModel.Update(TaskItem)`, outline tree-command setup and package compatibility relation assertion.
- Изменения: model-sync теперь полностью защищён `_isUpdatingFromModel`; UI test setup больше не запускает параллельный autosave при подготовке outline titles; package compatibility smoke проверяет актуальное repository state.
- Evidence:
  - `MainWindowViewModelTests/PasteTaskOutline_CreatesNestedTasksUnderCurrentTask` прошло 1/1.
  - `MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` прошло 1/1.
  - `MainControlTreeCommandsUiTests/TreeCommandUi_CopyTaskOutline_UsesCurrentFiltersAndSort` прошло 1/1.
  - `PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work` прошло 1/1.
  - Full `Unlimotion.Test` прошёл 570/570 вне managed sandbox: `C:\tmp\unlimotion-full-suite-sc0002-status-support-bdd-final2.log`.
- Stop rules: не нарушены; public UX/schema/AC/Gherkin/test annotations не менялись.
- Residual risks / follow-ups: нет известных full-suite blockers после финального прогона.

## Approval
Текущий STORM continuation уже переведён в EXEC фразой пользователя "спеку подтверждаю"; этот sub-scope нужен для закрытия обязательного full-suite gate.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | full-suite stability blocker | 0.88 | Нет | Перейти к EXEC | Нет | Да, пользователь подтвердил continuation | Full-suite gate блокирует завершение approved BDD slice | `specs/2026-06-29-taskitem-model-sync-autosave-stability.md` |
| EXEC | full-suite stability blocker | 0.92 | Нет | Commit после финальных checks | Нет | Да, continuation подтверждён | Root cause закрыт existing guard contract; full suite прошёл 570/570 | src/Unlimotion.ViewModel/TaskItemViewModel.cs; src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs; src/Unlimotion.Test/PackageUpdateCompatibilityUiTests.cs; docs/product/reports/* |
