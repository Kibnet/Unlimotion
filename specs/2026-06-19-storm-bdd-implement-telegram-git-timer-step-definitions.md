# SPEC: Исполняемые step definitions для Telegram Git timers

## Контекст

Текущий `/storm:cover` уже не содержит активных behavior coverage gaps, но BDD слой пока исполняет только один сценарий: `SC-0015-002`.

Следующий безопасный кандидат для расширения `/storm:bdd-implement` — `SC-0014-002`:

- Story: `ST-0014`
- Feature: `GF-014`
- Rule: `GR-040`
- Scenario: `SC-0014-002`
- Existing test evidence: `TS-0025`
- Feature file: `features/storm/st-0014-telegram-bot.feature`
- Existing test file: `src/Unlimotion.Test/TelegramBotGitTimerConflictSafetyTests.cs`

Сценарий уже подтвержден существующими TUnit тестами и не требует реального Telegram, сети, UI или Git remote:

```gherkin
Сценарий: Git timers пропускают pull и push во время разрешения конфликтов
  Дано у пользователя включены Telegram Git timers
  И в Git backup идет разрешение конфликтов
  Когда срабатывают pull и push timer события Telegram bot
  Тогда бот не выполняет pull и commit/push до завершения разрешения конфликтов.
```

## Цель

Добавить исполняемые BDD step definitions для `SC-0014-002`, сохранив существующее поведение, acceptance criteria и тестовые аннотации.

## Scope

В рамках EXEC разрешено:

- переиспользовать текущий repo-local BDD runner из `src/Unlimotion.Test/StormBdd`;
- добавить step definitions для шагов `SC-0014-002`;
- добавить отдельный executable spec test, который запускает сценарий из `.feature` файла;
- вынести общую проверочную логику Telegram Git timer safety в переиспользуемый test contract/helper, если это нужно для устранения дублирования между существующими тестами и BDD executable spec;
- обновить только STORM artifacts и reports, чтобы отразить новые Scenario -> Test -> Step Definition связи и behavior coverage metrics.

## Out of Scope

Запрещено в рамках этой SPEC:

- менять production code или поведение продукта;
- менять формулировки acceptance criteria;
- заменять acceptance criteria на Gherkin;
- менять существующие `.feature` сценарии без отдельного подтверждения;
- менять существующие test annotations;
- подключать внешний BDD framework;
- запускать реальные Telegram, сеть, Git remote или UI automation;
- исправлять Android/iOS `NETSDK1147` blocker.

## План реализации

1. Извлечь или переиспользовать существующую проверочную логику из `TelegramBotGitTimerConflictSafetyTests`.
2. Добавить `TelegramGitTimerStepDefinitions` в `src/Unlimotion.Test/StormBdd`.
3. Добавить executable spec test для `SC-0014-002`.
4. Синхронизировать `docs/product/storm.json`:
   - добавить новый `TS-*` для executable spec test;
   - добавить `SD-*` для четырех шагов сценария;
   - связать `SC-0014-002` с новым executable test и step definitions;
   - обновить BDD coverage metrics.
5. Обновить reports в `docs/product/reports`.
6. Не трогать production code и существующие annotations.

## Acceptance Criteria

- `SC-0014-002` получает исполняемые step definitions для всех четырех Gherkin steps.
- Новый executable spec test запускает сценарий из `features/storm/st-0014-telegram-bot.feature`.
- Существующие tests из `TS-0025` остаются рабочими и не теряют annotations.
- `storm.json` сохраняет существующие Vision, Product Goal, Needs, Constraints, Stories, AC, Tests, Code Units, conflicts, dependencies и coverage findings.
- Behavior coverage reports отражают второй step-executable scenario.
- Production code не изменен.

## Проверка

Минимальный validation set после EXEC:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/StormTelegramGitTimerExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/TelegramBotGitTimerConflictSafetyTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

Также проверить trailing spaces в измененных artifacts/test files.

## Stop Conditions

Остановиться и вернуться с отдельной QUEST/SPEC, если:

- для прохождения сценария нужна правка production code;
- требуется изменение существующих test annotations;
- существующий BDD runner не может корректно исполнить сценарий без расширения общего grammar/parsing контракта;
- обнаружится расхождение между `.feature`, `storm.json` и фактическим `TS-0025`, которое нельзя исправить artifact-only синхронизацией.

## Риски

- Возможна избыточная дубликация между обычными TUnit тестами и executable spec test. Снижение риска: вынести общий test contract/helper.
- Возможна слишком сильная привязка step definitions к русским формулировкам Gherkin. Снижение риска: ограничить mapping только текущим approved `.feature` сценарием.

## Журнал

| Время | Событие | Evidence |
| --- | --- | --- |
| 2026-06-19 | SPEC создана | `SC-0014-002`, `TS-0025`, `features/storm/st-0014-telegram-bot.feature` |
| 2026-06-19 | EXEC выполнен | `SD-0005..SD-0008`, `TS-0027`, targeted tests passed |
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
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTelegramGitTimerExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotGitTimerConflictSafetyTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

Результат: build прошел с существующими warnings; targeted tests прошли 1/1 и 3/3; STORM validator, `git diff --check` и trailing-space check должны быть подтверждены финальным validation pass.
