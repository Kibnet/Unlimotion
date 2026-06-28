# Agent CLI для чтения и изменения графа задач

## 0. Метаданные
- Тип (профиль): delivery-task / CLI tooling
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `feat/agent-task-cli`
- Ограничения: не менять UI; не менять persisted task schema; не подменять реальные правила доступности упрощенной логикой.
- Связанные ссылки: `README.md`, `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs`, `src/Unlimotion.Domain/TaskItem.cs`, `specs/unlimotion_file_mode.qnt`.

Если секция не применима, это указано явно в соответствующем разделе.

## 1. Overview / Цель
Добавить CLI, которым агент может напрямую читать и безопасно изменять каталог задач Unlimotion: получать список доступных задач, объяснение блокировок, диагностику структуры графа, менять статус задач и отмечать критерии выполнения.

Outcome contract:
- Success means: CLI собирается вместе с решением; read-команды `status`, `unlocked`, `task --explain`, `validate` и write-команды `set-status`, `complete`, `set-criterion`, `satisfy-criterion` работают на task directory без запуска UI.
- Итоговый артефакт / output: новый консольный проект и тестируемая библиотечная логика анализа доступности задач.
- Stop rules: остановиться, если потребуется изменить файловую схему задач, UI-контракт или правила статусов без отдельного подтверждения.

## 2. Текущее состояние (AS-IS)
- Правила доступности реализованы внутри `TaskTreeManager` приватными методами.
- `TaskItem` хранит четыре типа связей: `ParentTasks`, `ContainsTasks`, `BlockedByTasks`, `BlocksTasks`.
- `IsCanBeCompleted` пересчитывается как расчетное состояние: все contained child tasks завершены и нет незавершенных direct/inherited blockers.
- Переход в `InProgress` дополнительно запрещен будущей `PlannedBeginDate`.
- Переход в `Completed` дополнительно требует выполненных `CompletionCriteria`.
- Сейчас агентам приходится эмулировать правила вне Unlimotion, из-за чего легко ошибиться в логике `ContainsTasks` и наследуемых blocker-связей.

## 3. Проблема
Нет машинного read/write интерфейса, который возвращает статус задач, причины блокировки и безопасно фиксирует прогресс по тем же правилам, что использует Unlimotion.

## 4. Цели дизайна
- Разделение ответственности: CLI отвечает за аргументы/output, библиотечный analyzer отвечает за правила графа.
- Повторное использование: правила доступны без Avalonia/UI.
- Тестируемость: правила проверяются unit-тестами без файловой системы и UI.
- Консистентность: учитываются `ContainsTasks`, direct `BlockedByTasks`, inherited blockers через `ParentTasks`, criteria и planned begin.
- Обратная совместимость: persisted JSON tasks не меняются.

## 5. Non-Goals (чего НЕ делаем)
- Write-команды исключались только из первой безопасной итерации; текущий follow-up добавляет guarded write-mode.
- Не меняем поведение UI и существующие правила `TaskTreeManager`.
- Не мигрируем существующие задачи.
- Не вводим сетевой/API режим.
- Не исправляем граф знаний ТОС в этом коммите; CLI нужен как инструмент для следующих итераций.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.TaskTreeManager/TaskAvailabilityAnalyzer.cs` -> чистый анализ доступности и причин блокировок по коллекции `TaskItem`.
- `src/Unlimotion.Cli/` -> консольный проект с read/write командами и JSON/text output.
- `src/Unlimotion.Test/TaskAvailabilityAnalyzerTests.cs` -> regression tests для спорных правил.
- `src/Unlimotion.sln` -> включает CLI project.

### 6.2 Детальный дизайн
- CLI загружает и сохраняет task files из `--tasks <path>` через `Newtonsoft.Json`, чтобы соблюдать существующие `JsonIgnore`-контракты доменной модели; output JSON формируется через `System.Text.Json`.
- Команда `status` выводит агрегаты: total, counts by status, startable, completable, completed, archived.
- Команда `unlocked` выводит незавершенные задачи, которые можно начать сейчас.
- Команда `task --id <id> --explain` выводит статус, `canStart`, `canComplete`, `isCanBeCompleted` и причины.
- Команда `validate` выводит структурные дефекты: missing references, missing reverse links, stored/computed availability mismatch.
- Команда `set-status` меняет статус задачи с guard-правилами Unlimotion.
- Команда `complete` является shorthand для `set-status --status Completed` и требует `canComplete=true`.
- Команды `set-criterion` / `satisfy-criterion` меняют `CompletionCriteria[].IsSatisfied`.
- После каждой write-команды CLI пересчитывает `IsCanBeCompleted` для всех загруженных задач и сохраняет только изменившиеся task files.
- Output contract: `--format json` дает стабильный camelCase JSON для агентов; text format предназначен для человека; CLI project упаковывается как local/installable `dotnet tool` с command name `unlimotion-cli`.
- Visual planning artifact для UI-facing изменений: Не применимо, UI не меняется.
- UI test video evidence: Не применимо, автоматизация UI не меняется.
- Обработка ошибок: bad args и missing task id возвращают exit code `2`; validation issues возвращают `1`; успешные команды возвращают `0`.
- Производительность: analyzer строит lookup по id один раз, обходы используют visited sets для защиты от циклов.

## 7. Бизнес-правила / Алгоритмы
- `containedChildrenComplete(task)`: все существующие `ContainsTasks` должны иметь статус, не являющийся incomplete for availability.
- `blockersComplete(task)`: все существующие `BlockedByTasks` у самой задачи и у всех ancestor tasks через `ParentTasks` должны быть complete for availability.
- `isCanBeCompleted = containedChildrenComplete && blockersComplete`.
- `canStart = isCanBeCompleted && PlannedBeginDate не в будущем && Status не Completed/Archived`.
- `canComplete = isCanBeCompleted && все CompletionCriteria satisfied && Status не Completed/Archived`.
- Missing references не блокируют расчет доступности, чтобы совпадать с текущим tolerant runtime-поведением, но попадают в `validate`.

## 8. Точки интеграции и триггеры
- CLI вызывается явно из shell: `dotnet run --project src/Unlimotion.Cli -- <command> ...`.
- Runtime/UI пересчет задач не меняется.
- Analyzer может быть переиспользован `TaskTreeManager` в будущем, но это не часть текущей итерации.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Новых migration steps нет.
- `IsCanBeCompleted` остается persisted/calculated полем существующей модели; CLI только сравнивает stored vs computed.

## 10. Миграция / Rollout / Rollback
- Rollout: добавить проект и тесты в solution.
- Backward compatibility: существующие task files читаются без изменений.
- Rollback: удалить CLI project, analyzer/tests и запись project в solution.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `dotnet build src/Unlimotion.sln -c Release` проходит.
  - Targeted TUnit tests на analyzer проходят.
  - CLI возвращает JSON для `status`, `unlocked`, `task --explain`, `validate` на существующем каталоге задач.
  - Тесты фиксируют правило: незавершенный child блокирует parent.
  - Тесты фиксируют правило: blocker parent наследуется child task.
  - Тесты фиксируют правило: completion criteria требуются для `canComplete`, но не для `isCanBeCompleted`.
- Visual acceptance: Не применимо, UI не меняется.
- UI video evidence: Не применимо, UI не меняется.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/TaskAvailabilityAnalyzerTests/*"`
  - `dotnet build src/Unlimotion.sln -c Release`
  - `dotnet run --project src/Unlimotion.Cli -c Release -- status --tasks <task-dir> --format json`
  - `dotnet pack src/Unlimotion.Cli/Unlimotion.Cli.csproj -c Release -o <pack-dir>`
  - `dotnet tool install --tool-path <tool-path> --add-source <pack-dir> Unlimotion.Cli --version 0.3.0`
- Stop rules: если тестовый фильтр не находит тесты, сначала получить список TUnit tests; не запускать тяжелые параллельные UI suites без необходимости.

## 12. Риски и edge cases
- Risk: analyzer расходится с приватной логикой `TaskTreeManager`. Mitigation: правила перенесены из inspected source и покрыты regression tests.
- Risk: циклы `ParentTasks` могут вызвать рекурсию. Mitigation: visited set.
- Risk: task files имеют дополнительные поля. Mitigation: `Newtonsoft.Json` соблюдает текущие атрибуты доменной модели; CLI validation/smoke проверяет сохранение на копии графа.
- Risk: CLI может повредить task files при записи. Mitigation: write-команды guard-ятся правилами availability, пишут через Newtonsoft.Json, пересчитывают `IsCanBeCompleted` и валидируются smoke-тестом на копии графа.

## 13. План выполнения
1. Добавить SPEC и зафиксировать scope.
2. Добавить pure analyzer для доступности и validation primitives.
3. Добавить CLI project с командами `status`, `unlocked`, `task`, `validate`.
4. Добавить focused tests на правила доступности.
5. Прогнать targeted tests, build и smoke CLI.
6. Закоммитить результат.
7. Follow-up: добавить guarded write-команды и проверить их на копии графа.

## 14. Открытые вопросы
Нет блокирующих вопросов. Write-команды добавлены как follow-up после подтверждения read-mode semantics.

## 15. Соответствие профилю
- Профиль: delivery-task / CLI tooling.
- Выполненные требования профиля: scope зафиксирован; источник правил указан; UI evidence помечен как не применимый; проверки и rollback описаны.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-28-agent-task-cli.md` | Новый SPEC | Зафиксировать контракт и границы |
| `src/Unlimotion.TaskTreeManager/TaskAvailabilityAnalyzer.cs` | Новый analyzer | Дать CLI переиспользуемые правила |
| `src/Unlimotion.Cli/*` | CLI project, dotnet tool metadata, read/write commands, usage README | Машинный доступ агентов к графу |
| `src/Unlimotion.Test/TaskAvailabilityAnalyzerTests.cs` | Новые tests | Regression coverage спорных правил |
| `src/Unlimotion.sln` | Добавить project | Сборка CLI вместе с решением |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Агентский доступ | Только внешняя эмуляция JSON | CLI с объяснениями по правилам Unlimotion |
| Диагностика блокировок | Ручной анализ | `task --explain` |
| Проверка графа | Ручные скрипты | `validate` |
| Правила доступности | Приватные методы UI/runtime manager | Pure analyzer для read/write tooling |

## 18. Альтернативы и компромиссы
- Вариант: писать отдельный скрипт в Knowledge.TOC.
- Плюсы: быстрее локально.
- Минусы: снова расходится с Unlimotion и не живет рядом с source of truth.
- Почему выбранное решение лучше: CLI в Unlimotion можно использовать для любого графа задач и покрыть тестами вместе с реальными моделями.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, non-goals описаны. |
| B. Качество дизайна | 6-10 | PASS | Ответственности, контракты, модель и rollback определены. |
| C. Безопасность изменений | 11-13 | PASS | UI/schema не меняются; план проверок есть. |
| D. Проверяемость | 14-16 | PASS | Есть acceptance criteria, команды и таблица файлов. |
| E. Готовность к автономной реализации | 17-19 | PASS | Открытых блокеров нет. |
| F. Соответствие профилю | 20 | PASS | CLI/non-UI профиль соблюден. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Read/write CLI, write-команды guard-ятся правилами availability. |
| 2. Понимание текущего состояния | 5 | Указаны реальные правила TaskTreeManager. |
| 3. Конкретность целевого дизайна | 5 | Названы компоненты, команды, output и exit codes. |
| 4. Безопасность (миграция, откат) | 5 | Нет schema/UI migration; rollback простой. |
| 5. Тестируемость | 5 | Есть focused TUnit и smoke CLI. |
| 6. Готовность к автономной реализации | 5 | Нет открытых blockers. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-28-agent-task-cli.md`, instruction stack, selected profile, planned changed files.
- Decision: можно выполнять; пользователь уже дал команду `выполняй план` и затем `продолжай`, поэтому отдельная остановка на approval не требуется.
- Review passes:
  - Scope/Evidence pass: PASS, только non-UI CLI и tests.
  - Contract pass: PASS, persisted task schema не меняется.
  - Adversarial risk pass: PASS, главная опасность расхождения с runtime покрыта tests.
  - Re-review after fixes / Fix and re-review: не требуется.
  - Stop decision: продолжать EXEC.
- Evidence inspected: `README.md`, `TaskTreeManager.cs`, `TaskItem.cs`, `unlimotion_file_mode.qnt`.
- Depth checklist:
  - Scope drift / unrelated changes: не выявлено.
  - Acceptance criteria: есть.
  - Validation evidence: запланирована.
  - Unsupported claims: нет.
  - Regression / edge case: cycles/missing references учтены.
  - Comments/docs/changelog: changelog не требуется для internal CLI.
  - Hidden contract change: не ожидается.
  - Manual-review challenge: вероятный вопрос - почему нет write-команд; ответ: в первой итерации исключались как non-goal для безопасного MVP, затем добавлены отдельным guarded follow-up.
- No-findings justification: spec не меняет продуктовый UI/runtime и фиксирует проверяемые правила.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| Нет находок | scope | Нет блокирующих проблем | Продолжить EXEC | fixed |

- Fixed before continuing: не требуется.
- Checks rerun: не применимо до EXEC.
- Needs human: нет.
- Residual risks / follow-ups: write-mode CLI выполнен отдельной guarded итерацией; остается риск расхождения analyzer с будущими изменениями `TaskTreeManager`.

### Post-EXEC Review
- Статус: PASS с окруженческим ограничением для full solution build
- Scope reviewed: approved spec, `git status --short`, relevant diff, targeted tests, CLI smoke output.
- Decision: можно завершать текущую CLI-итерацию; full `src/Unlimotion.sln` build не является валидным локальным gate без Android workload.
- Review passes:
  - Scope/Evidence pass: PASS, изменены только SPEC, CLI project, analyzer, tests и solution entry.
  - Contract pass: PASS, task JSON schema и UI не менялись.
  - Adversarial risk pass: PASS, спорные правила `ContainsTasks`, inherited blockers и criteria покрыты tests.
  - Re-review after fixes / Fix and re-review: не требовалось после успешного focused run.
  - Stop decision: закоммитить CLI MVP; write-mode выполнить отдельным follow-up.
- Evidence inspected:
  - `dotnet build src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release` -> PASS, 0 warnings, 0 errors.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/TaskAvailabilityAnalyzerTests/*"` -> PASS, 4/4.
  - `dotnet run --project src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -- status --tasks C:\Projects\ТОС\Knowledge.TOC\Tasks --format json` -> PASS, 264 tasks.
  - `dotnet run --project src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -- unlocked --tasks C:\Projects\ТОС\Knowledge.TOC\Tasks --format json` -> PASS, 0 unlocked tasks in current graph state.
  - `dotnet run --project src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -- task --tasks C:\Projects\ТОС\Knowledge.TOC\Tasks --id <sample> --explain --format json` -> PASS, explanation returned.
  - `dotnet run --project src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -- validate --tasks C:\Projects\ТОС\Knowledge.TOC\Tasks --format json` -> command works, exit 1 because graph has 6 stored/computed availability mismatches.
  - `dotnet build src\Unlimotion.sln -c Release` -> BLOCKED by existing environment issue `NETSDK1147` missing Android workload before product compile.
- Depth checklist:
  - Scope drift / unrelated changes: нет.
  - Acceptance criteria: выполнены, кроме full solution build, который заблокирован окружением.
  - Validation evidence: зафиксирована.
  - Unsupported claims: нет.
  - Regression / edge case: cycles protected by visited set; missing references are validation issues, not runtime blockers.
  - Comments/docs/changelog: отдельный changelog не нужен для internal CLI.
  - Hidden contract change: нет.
  - Manual-review challenge: главный риск - analyzer может разойтись с private `TaskTreeManager`; mitigated by прямое копирование правил и focused tests.
- No-findings justification: изменения additive, write-операции guard-ятся правилами availability и не меняют persisted schema или UI.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full solution build локально блокируется `NETSDK1147` из-за отсутствующего Android workload | Зафиксировать как environment blocker; использовать CLI build + targeted tests как evidence | accepted-risk |

- Fixed before final report: конфликт `TaskStatus` и `StartsWith(char, StringComparison)` в CLI исправлены до passing build.
- Checks rerun: CLI build, focused TUnit, CLI smoke.
- Validation evidence: перечислена выше.
- Unrelated changes: не обнаружены.
- Needs human: нет.
- Residual risks / follow-ups: прямое переиспользование analyzer внутри `TaskTreeManager` можно вынести в отдельную итерацию.
#### Dotnet tool packaging follow-up
- `dotnet pack src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -o C:\tmp\unlimotion-cli-pack-20260628` -> PASS, package `Unlimotion.Cli.0.1.0.nupkg` created.
- `dotnet tool install --tool-path C:\tmp\unlimotion-cli-tool-20260628 --add-source C:\tmp\unlimotion-cli-pack-20260628 Unlimotion.Cli --version 0.1.0` -> PASS, command `unlimotion-cli` installed.
- `C:\tmp\unlimotion-cli-tool-20260628\unlimotion-cli.exe status --tasks C:\Projects\ТОС\Knowledge.TOC\Tasks --format json` -> PASS, 264 tasks.
- `C:\tmp\unlimotion-cli-tool-20260628\unlimotion-cli.exe validate --tasks C:\Projects\ТОС\Knowledge.TOC\Tasks --format json` -> command works, exit 1 because the graph has 6 stored/computed availability mismatches.

#### Guarded write-mode follow-up
- `dotnet restore src\Unlimotion.Cli\Unlimotion.Cli.csproj` -> PASS.
- `dotnet build src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release --no-restore` -> PASS, 0 warnings, 0 errors.
- `dotnet pack src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release --no-restore -o C:\tmp\unlimotion-cli-pack-20260628-write-current` -> PASS, package `Unlimotion.Cli.0.3.0.nupkg` created.
- `dotnet tool install --tool-path C:\tmp\unlimotion-cli-tool-20260628-write-current --add-source C:\tmp\unlimotion-cli-pack-20260628-write-current Unlimotion.Cli --version 0.3.0` -> PASS, command `unlimotion-cli` installed.
- Smoke on copied `Knowledge.TOC\Tasks`: completing before criteria is denied; satisfying all criteria then `complete` succeeds; saved `Status` remains string enum; copied graph validates with 0 mismatches and unlocks the next task.
- `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/TaskAvailabilityAnalyzerTests/*"` -> PASS, 4/4.

## Approval
Пользовательская команда `выполняй план` и последующее `продолжай` считаются подтверждением выполнения этой спеки в текущем контексте.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Зафиксировать CLI MVP | 0.9 | Нет | Реализовать analyzer и CLI | Нет | Да, пользователь сказал `выполняй план` / `продолжай` | Без CLI агенты продолжают ошибаться в правилах разблокировки | `specs/2026-06-28-agent-task-cli.md` |
| EXEC | Реализовать CLI MVP | 0.85 | Нет данных о write-mode требованиях | Закоммитить read-only CLI | Нет | Нет | Read-only инструмент уже достаточен для диагностики ошибок графа | `src/Unlimotion.Cli`, `TaskAvailabilityAnalyzer.cs`, `TaskAvailabilityAnalyzerTests.cs`, `src/Unlimotion.sln` |
| EXEC | Упаковать CLI как dotnet tool | 0.9 | Нет | Проверить pack/install/smoke и закоммитить | Нет | Нет | Агентам нужен стабильный installed command вместо `dotnet run --project` | `src/Unlimotion.Cli/Unlimotion.Cli.csproj`, `src/Unlimotion.Cli/README.md`, `specs/2026-06-28-agent-task-cli.md` |
| EXEC | Добавить guarded write-команды CLI | 0.9 | Нет | Закоммитить write-mode | Нет | Нет | Агентам нужно менять статусы и критерии тем же инструментом, которым они читают availability | `src/Unlimotion.Cli/Program.cs`, `src/Unlimotion.Cli/README.md`, `src/Unlimotion.Cli/Unlimotion.Cli.csproj`, `specs/2026-06-28-agent-task-cli.md` |
