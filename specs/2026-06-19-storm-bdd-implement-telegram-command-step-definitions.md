# SPEC: Исполняемые step definitions для Telegram command/auth

## Контекст

Текущий BDD layer уже исполняет два сценария:

- `SC-0015-002` через `SD-0001..SD-0004` и `TS-0026`;
- `SC-0014-002` через `SD-0005..SD-0008` и `TS-0027`.

Следующий безопасный кандидат для расширения `/storm:bdd-implement` — `SC-0014-001`:

- Story: `ST-0014`
- Feature: `GF-014`
- Rule: `GR-039`
- Scenario: `SC-0014-001`
- Existing test evidence: `TS-0022`
- Feature file: `features/storm/st-0014-telegram-bot.feature`
- Existing test file: `src/Unlimotion.Test/TelegramBotCommandAuthorizationTests.cs`

Сценарий уже подтвержден существующими TUnit тестами и не требует реального Telegram API, credentials, polling, network или UI:

```gherkin
Сценарий: Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.
  Дано у пользователя открыт актуальный набор задач Unlimotion
  И поведение относится к истории ST-0014
  Когда пользователь обращается к задачам через Telegram bot
  Тогда Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.
```

## Цель

Добавить исполняемые BDD step definitions для `SC-0014-001`, сохранив существующее поведение, acceptance criteria, `.feature` wording и test annotations.

## Scope

В рамках EXEC разрешено:

- переиспользовать текущий repo-local BDD runner из `src/Unlimotion.Test/StormBdd`;
- добавить step definitions для четырех шагов `SC-0014-001`;
- добавить отдельный executable spec test, который запускает сценарий из `.feature` файла;
- вынести общую проверочную логику `TelegramBotCommandAuthorizationTests` в переиспользуемый test contract/helper, если это нужно для устранения дублирования;
- обновить только STORM artifacts и reports, чтобы отразить новые Scenario -> Test -> Step Definition связи и behavior coverage metrics.

## Out of Scope

Запрещено в рамках этой SPEC:

- менять production code или поведение продукта;
- менять формулировки acceptance criteria;
- заменять acceptance criteria на Gherkin;
- менять существующий `.feature` wording;
- менять существующие test annotations;
- подключать внешний BDD framework;
- запускать реальный Telegram API, polling, credentials, network, Git remote или UI automation;
- расширять callback сценарий `SC-0014-003`;
- исправлять Android/iOS `NETSDK1147` blocker.

## План реализации

1. Вынести или переиспользовать deterministic command/auth checks из `TelegramBotCommandAuthorizationTests`.
2. Добавить `TelegramCommandStepDefinitions` в `src/Unlimotion.Test/StormBdd`.
3. Добавить executable spec test для `SC-0014-001`.
4. Синхронизировать `docs/product/storm.json`:
   - добавить новый `TS-*` для executable spec test;
   - добавить `SD-*` для четырех шагов сценария;
   - связать `SC-0014-001` с новым executable test и step definitions;
   - обновить BDD coverage metrics с `2/45` до `3/45` step-executable scenarios.
5. Обновить reports в `docs/product/reports`.
6. Не трогать production code и existing test annotations.

## Acceptance Criteria

- `SC-0014-001` получает исполняемые step definitions для всех четырех Gherkin steps.
- Новый executable spec test запускает сценарий из `features/storm/st-0014-telegram-bot.feature`.
- Существующие tests из `TS-0022` остаются рабочими и не теряют annotations.
- `storm.json` сохраняет существующие Vision, Product Goal, Needs, Constraints, Stories, AC, Tests, Code Units, conflicts, dependencies и coverage findings.
- Behavior coverage reports отражают третий step-executable scenario.
- Production code не изменен.

## Проверка

Минимальный validation set после EXEC:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTelegramCommandExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCommandAuthorizationTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

Также проверить trailing spaces в измененных artifacts/test files.

## Stop Conditions

Остановиться и вернуться с отдельной QUEST/SPEC, если:

- для прохождения сценария нужна правка production code;
- требуется изменение существующих test annotations;
- требуется изменение `.feature` wording или acceptance criteria;
- текущий BDD runner не может корректно исполнить сценарий без расширения общего grammar/parsing контракта;
- обнаружится расхождение между `.feature`, `storm.json` и фактическим `TS-0022`, которое нельзя исправить artifact sync без изменения поведения.

## Риски

- Сценарий агрегирует несколько command paths (`/start`, `/help`, `/search`, `/task`, `/root`), поэтому step definition может стать слишком широкой. Снижение риска: использовать существующий deterministic command/auth contract и не добавлять real Telegram integration.
- Возможна избыточная дубликация между обычными TUnit tests и executable spec test. Снижение риска: вынести общий test contract/helper.

## Журнал

| Время | Событие | Evidence |
| --- | --- | --- |
| 2026-06-19 | SPEC создана | `SC-0014-001`, `TS-0022`, `features/storm/st-0014-telegram-bot.feature` |
| 2026-06-19 | EXEC выполнен | `SD-0009..SD-0012`, `TS-0028`, targeted tests passed |
| 2026-06-19 | `/storm:bdd-sync` и `/storm:bdd-lint` актуализированы | `docs/product/storm.json`, `docs/product/reports/*` |

## Gate

Статус: `EXEC completed`

Переход в EXEC подтвержден фразой:

```text
Спеку подтверждаю
```

## Post-EXEC Validation

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTelegramCommandExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCommandAuthorizationTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

Результат: build прошел с существующими warnings; targeted tests прошли 1/1 и 7/7; STORM validator, `git diff --check` и trailing-space check должны быть подтверждены финальным validation pass.
