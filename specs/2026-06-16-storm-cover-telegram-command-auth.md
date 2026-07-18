# STORM cover ST-0014 Telegram command/auth coverage

## 0. Метаданные
- Тип (профиль): QUEST delivery-task / storm-product-development
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: не менять публичный Telegram command contract без отдельного решения; не менять STORM acceptance criteria; не добавлять test annotations без отдельного подтверждения
- Связанные ссылки: `docs/product/storm.json`, `docs/product/reports/coverage.md`, `features/storm/st-0014-telegram-bot.feature`

Если пользователь подтверждает эту SPEC, это означает product decision: Telegram bot считается поддерживаемой продуктовой поверхностью для `ST-0014/CV-0003` command/auth coverage.

## 1. Overview / Цель
Закрыть следующий `/storm:cover` gap `CV-0003` для `ST-0014`: подтвердить command/auth behavior Telegram bot автоматическими тестами и связать evidence с `AC-0039` / `SC-0014-001`.

Outcome contract:
- Success means: `AC-0039` получает automated/passing evidence для allowed users, unauthorized access restriction и базовых команд `/start`, `/help`, `/search`, `/task`, `/root`.
- Итоговый артефакт / output: тестовый seam для Telegram command handling, targeted tests, обновлённые STORM artifacts/reports/feature tags.
- Stop rules: если для тестов нужен реальный Telegram API, реальные credentials, сетевой polling или Git side effects, остановиться и сузить seam; если обнаружится поведенческое изменение команд, остановиться и запросить product decision.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion.TelegramBot/Bot.cs` содержит static Telegram bot orchestration, private message/callback handlers, `AllowedUsers`, `TelegramBotClient`, `TaskService` и Git timers.
- `CheckAccess` запрещает unknown users через early return и лог, не выполняя task storage access.
- `/start`, `/help`, `/search`, `/task`, `/root` реализованы внутри private `OnMessageReceived`.
- В `src/Unlimotion.Test` нет Telegram-specific tests.
- `AC-0039` и `SC-0014-001` остаются draft без test links.

## 3. Проблема
Telegram command/auth behavior уже есть в коде, но не имеет автоматизированного product evidence. Из-за static/private Telegram client coupling поведение нельзя надёжно проверить без test seam.

## 4. Цели дизайна
- Разделение ответственности: отделить pure command handling от Telegram polling/startup.
- Повторное использование: production `Bot` должен использовать тот же handler, который покрывается тестами.
- Тестируемость: тесты не должны обращаться к Telegram API, Git remote, реальному polling или бесконечному `Task.Delay`.
- Консистентность: сохранить текущие русские ответы команд и allowed-user gate.
- Обратная совместимость: существующий `Bot.StartAsync` и public entrypoint не ломать.

## 5. Non-Goals (чего НЕ делаем)
- Не покрываем callback flows `CV-0004` (`CreateSub`, `SetStatus`, relation buttons, Git timers).
- Не меняем формат callback data.
- Не добавляем real Telegram integration tests.
- Не меняем storage model, Git behavior или server storage.
- Не исправляем product wording команд сверх необходимого для тестового seam.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.TelegramBot/Bot.cs` -> остаётся startup/polling composition root, делегирует command handling в тестируемый компонент.
- `src/Unlimotion.TelegramBot/TelegramCommandHandler.cs` -> новый internal handler для allowed-user gate и команд `/start`, `/help`, `/search`, `/task`, `/root`.
- `src/Unlimotion.TelegramBot/ITelegramMessageSink.cs` или эквивалентный минимальный adapter -> отправка сообщений/списков/keyboard без прямой зависимости тестов от network Telegram client.
- `src/Unlimotion.Test/TelegramBotCommandAuthorizationTests.cs` -> TUnit tests для `AC-0039`.
- `docs/product/storm.json`, `docs/product/reports/*`, `features/storm/st-0014-telegram-bot.feature` -> связать `SC-0014-001` с новым `TS-0022`.

### 6.2 Детальный дизайн
- Handler принимает allowed users, task query service/read model и message sink.
- Unauthorized user path должен завершаться до обращения к task service/storage.
- Allowed `/start` отправляет приветствие.
- Allowed `/help` отправляет список команд.
- Allowed `/search query` вызывает поиск и возвращает результаты с open callback buttons либо текст "Задачи не найдены.".
- Allowed `/task id` возвращает задачу или "Задача не найдена".
- Allowed `/root` возвращает root tasks или "Задачи не найдены.".
- Visual planning artifact: Не применимо, это Telegram text/API behavior, не Avalonia UI.
- UI test video evidence: Не применимо, это не UI automation task; fallback evidence: targeted TUnit command/auth tests.
- Границы сохранения поведения: русские тексты и command prefixes сохраняются; если handler extraction выявит неоднозначность current behavior, не менять ответ без отдельного product decision.
- Обработка ошибок: существующий user-facing fallback "Произошла ошибка при обработке вашего запроса." сохраняется.
- Производительность: handler должен быть in-memory и не запускать timers/polling в тестах.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Только `AllowedUsers` могут вызывать task commands.
- Unauthorized user не должен читать, искать, открывать или изменять задачи.
- Help/start/search/task/root являются command/auth минимальным контрактом `AC-0039`.
- Пустой или неизвестный command не должен обращаться к storage сверх необходимого command parsing.

## 8. Точки интеграции и триггеры
- `Bot.HandleUpdateAsync` / `OnMessageReceived` должны делегировать text messages в новый handler.
- Existing callback handling остаётся в `Bot.cs` и не входит в эту SPEC.
- `TaskService.SearchTasks`, `TaskService.GetTask`, `TaskService.RootTasks` используются через минимальный read interface/adapter.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- In-memory user state для command/auth tests не должен менять storage schema.
- Git repository state не затрагивается.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется.
- Rollout: handler extraction должен быть transparent для `Program.cs` и `Bot.StartAsync`.
- Rollback: удалить новый handler/tests и вернуть message handling в прежний static path.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `AC-0039` получает automated/passing evidence.
  - Unauthorized user не вызывает task query service и не получает task data.
  - `/start` и `/help` возвращают текущие русские тексты.
  - `/search` для allowed user возвращает matching task list/buttons либо "Задачи не найдены.".
  - `/task` возвращает найденную задачу или "Задача не найдена".
  - `/root` возвращает root tasks либо "Задачи не найдены.".
- Какие тесты добавить/изменить:
  - Добавить `TelegramBotCommandAuthorizationTests`.
  - Production tests/UI tests не менять вне Telegram scope.
- Characterization tests:
  - Зафиксировать текущие тексты команд перед любыми refactor edits.
- Visual acceptance: Не применимо.
- UI video evidence: Не применимо; fallback evidence: targeted TUnit output.
- Базовые замеры performance: Не применимо.
- Команды для проверки:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCommandAuthorizationTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
- Stop rules:
  - Не запускать реальный Telegram polling.
  - Не требовать real bot token.
  - Не менять callbacks/Git timers в рамках `CV-0003`.

## 12. Риски и edge cases
- Static `Bot.cs` может потребовать аккуратного extraction, чтобы не изменить runtime startup.
- Telegram.Bot concrete API может усложнить adapter; держать adapter минимальным.
- Текущий unauthorized path не отправляет user-facing denial text; тестировать именно отсутствие storage access, если product decision не требует текста отказа.
- `/search` без query может иметь fragile parsing; если текущий код падает, оформить отдельный finding вместо скрытого behavior change.

## 13. План выполнения
1. Добавить characterization tests/fixture для allowed и unauthorized command handling.
2. Извлечь минимальный handler/adapter из `Bot.cs`.
3. Добавить tests для `/start`, `/help`, `/search`, `/task`, `/root`, unauthorized access.
4. Запустить targeted tests и build.
5. Обновить `storm.json`, `features/storm/st-0014-telegram-bot.feature`, reports: `SC-0014-001 -> TS-0022`, статус passing при фактическом pass.
6. Запустить STORM validator и `git diff --check`.

## 14. Открытые вопросы
Нет блокирующих. Подтверждение SPEC одновременно подтверждает Telegram bot как supported surface для `CV-0003`.

## 15. Соответствие профилю
- Профиль: storm-product-development + central QUEST delivery gate.
- Выполненные требования профиля: SPEC до EXEC, product artifacts на русском, acceptance criteria не заменяются Gherkin, test/code changes только после подтверждения.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.TelegramBot/Bot.cs` | Делегировать message command handling в testable handler | Снять static/private Telegram API coupling |
| `src/Unlimotion.TelegramBot/TelegramCommandHandler.cs` | Новый handler для allowed-user gate и базовых commands | Проверяемый контракт `AC-0039` |
| `src/Unlimotion.Test/TelegramBotCommandAuthorizationTests.cs` | Новые TUnit tests | Закрыть `CV-0003` |
| `docs/product/storm.json` | Добавить `TS-0022`, связать `SC-0014-001` и metrics | STORM traceability |
| `features/storm/st-0014-telegram-bot.feature` | Добавить `@test:TS-0022`, status evidence при pass | BDD sync |
| `docs/product/reports/*.md` | Обновить coverage/bdd/trace/ranking | Актуальные отчёты |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `AC-0039` | partial, tests отсутствуют | automated/passing при успешном `TS-0022` |
| `SC-0014-001` | draft без test links | linked to `TS-0022`, status passing |
| Telegram command handling | private static path, тяжело тестировать | production path использует testable handler |

## 18. Альтернативы и компромиссы
- Вариант: reflection tests против private static methods.
- Плюсы: меньше production edits.
- Минусы: brittle, всё равно упирается в concrete `TelegramBotClient`.
- Почему выбранное решение лучше в контексте этой задачи: minimal handler extraction даёт устойчивый contract test без network/API credentials и сохраняет runtime behavior.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, границы и non-goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Есть минимальный seam, rollout и rollback. |
| C. Безопасность изменений | 11-13 | PASS | Команды проверки и stop rules заданы. |
| D. Проверяемость | 14-16 | PASS | Open questions не блокируют; файлы перечислены. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало и компромиссы описаны. |
| F. Соответствие профилю | 20 | PASS | QUEST gate соблюдён. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | `CV-0003` и `AC-0039` зафиксированы. |
| 2. Понимание текущего состояния | 5 | Static Telegram coupling и отсутствие tests описаны. |
| 3. Конкретность целевого дизайна | 5 | Handler/adapter/test plan определены. |
| 4. Безопасность (миграция, откат) | 5 | No migration, rollback прост. |
| 5. Тестируемость | 5 | Targeted tests и команды заданы. |
| 6. Готовность к автономной реализации | 5 | Файлы, порядок и stop rules достаточны. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-storm-cover-telegram-command-auth.md`, central stack, `storm-product-development`, planned changed files
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: PASS
  - Contract pass: PASS
  - Adversarial risk pass: PASS
  - Re-review after fixes / Fix and re-review: Не применимо
  - Stop decision: ждать подтверждения
- Evidence inspected: `docs/product/storm.json`, `docs/product/reports/coverage.md`, `src/Unlimotion.TelegramBot/Bot.cs`, `src/Unlimotion.TelegramBot/TaskService.cs`
- Depth checklist:
  - Scope drift / unrelated changes: ограничено `ST-0014/CV-0003`
  - Acceptance criteria: `AC-0039`
  - Validation evidence: команды заданы, не запускались до EXEC
  - Unsupported claims: нет
  - Regression / edge case: unauthorized path и real Telegram API stop rules описаны
  - Comments/docs/changelog: не требуется
  - Hidden contract change: запрещён stop rule
  - Manual-review challenge: проверить, что extraction не меняет текущие русские ответы и не запускает timers в tests
- No-findings justification: SPEC достаточно узкая и проверяемая; главный риск вынесен в stop rules.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | risk | Unauthorized path сейчас может быть silent denial; это надо зафиксировать как current behavior, если product не требует текста отказа. | Не менять без отдельного product decision. | accepted-risk |

- Fixed before continuing: Не применимо
- Checks rerun: Не применимо до EXEC
- Needs human: подтверждение SPEC
- Residual risks / follow-ups: `CV-0004` callbacks/Git timers останутся отдельным gap.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, relevant code/test/artifact diff, targeted build/test evidence, STORM validator evidence
- Decision: можно завершать
- Review passes:
  - Scope/Evidence pass: PASS
  - Contract pass: PASS
  - Adversarial risk pass: PASS
  - Re-review after fixes / Fix and re-review: fixed compile/usings/assertion warnings, reran build and targeted tests
  - Stop decision: завершать текущий EXEC
- Evidence inspected: `src/Unlimotion.TelegramBot/Bot.cs`, `src/Unlimotion.TelegramBot/TelegramCommandHandler.cs`, `src/Unlimotion.Test/TelegramBotCommandAuthorizationTests.cs`, `docs/product/storm.json`, `features/storm/st-0014-telegram-bot.feature`, `docs/product/reports/*`
- Depth checklist:
  - Scope drift / unrelated changes: `CV-0004` callbacks/Git timers не трогались; prior `ST-0011` worktree changes сохранены
  - Acceptance criteria: `AC-0039` переведён в `full`, `AC-0040` оставлен partial/draft
  - Validation evidence: restore/build/targeted tests/STORM validator/diff check выполнены
  - Unsupported claims: reports отражают только passing `TS-0022`, без claim на callbacks
  - Regression / edge case: unauthorized user path проверен на отсутствие task query/storage access
  - Comments/docs/changelog: changelog не применим; STORM reports обновлены
  - Hidden contract change: русские тексты `/start`, `/help`, not-found/unknown command сохранены в tests
  - Manual-review challenge: проверить, что `Bot.StartAsync` не запускается в tests и handler не требует Telegram credentials
- No-findings justification: изменения ограничены approved seam extraction + command/auth tests + STORM links; targeted evidence зелёный.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | follow-up | `CV-0004` callbacks/status/relation/Git timers не входят в эту SPEC. | Планировать отдельную SPEC после `CV-0003`, если Telegram bot остаётся supported. | follow-up |

- Fixed before final report: added explicit `System*` usings; replaced deprecated `HasCount` assertions; normalized `AllowedUsers` null fallback
- Checks rerun: build, targeted Telegram tests, STORM validator, `git diff --check`
- Validation evidence: `dotnet restore src/Unlimotion.Test/Unlimotion.Test.csproj`; `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`; `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCommandAuthorizationTests/*" --output Detailed`
- Unrelated changes: ранее существующие ST-0011/TS-0020 изменения в worktree не входят в эту SPEC
- Needs human: нет
- Residual risks / follow-ups: `CV-0004`

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | STORM cover planning | 0.86 | Подтверждение, что Telegram bot supported для CV-0003 | Ждать "Спеку подтверждаю" | да | нет | Следующий открытый gap после CV-0006 — ST-0014/CV-0003; test/code changes требуют QUEST gate. | `specs/2026-06-16-storm-cover-telegram-command-auth.md` |
| EXEC | command/auth coverage | 0.88 | Нет для CV-0003; CV-0004 остаётся follow-up | Завершить отчёт и предложить следующий gate | нет | да, пользователь подтвердил SPEC | Извлечён handler seam без real Telegram API; `TS-0022` прошёл 7/7 и закрыл `AC-0039`. | `src/Unlimotion.TelegramBot/TelegramCommandHandler.cs`, `src/Unlimotion.TelegramBot/Bot.cs`, `src/Unlimotion.Test/TelegramBotCommandAuthorizationTests.cs`, `docs/product/storm.json`, `features/storm/st-0014-telegram-bot.feature`, `docs/product/reports/*` |
