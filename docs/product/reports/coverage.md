# STORM Coverage Analysis

Сгенерировано: 2026-06-19
Команда: `/storm:bdd-implement SC-0014-001 executable step definitions`
Режим: `delivery-task` после подтвержденной SPEC; production code и test annotations не менялись

## Область

Эта итерация добавляет executable BDD slice для `ST-0014 / AC-0039 / SC-0014-001`: сценарий исполняется из `.feature` текста через repo-local step definitions `SD-0009..SD-0012` и TUnit evidence `TS-0028`. Ранее реализованные slices `SC-0015-002 -> SD-0001..SD-0004 -> TS-0026` и `SC-0014-002 -> SD-0005..SD-0008 -> TS-0027` сохранены.

Acceptance criteria не заменялись на Gherkin. Существующие stories, tests, conflicts, dependencies и решение по `CV-0007` сохранены.

## Сводка

| Метрика | Значение |
| --- | --- |
| Acceptance criteria всего | 44 |
| AC с тестовыми связями | 44 |
| AC с уровнем full/critical | 44 |
| AC с уровнем partial | 0 |
| AC без тестовых связей | 0 |
| Active cover/behavior gaps | 0 |
| Scenario -> Test links | 45/45 |
| Draft scenarios | 0 |
| Passing scenarios | 7 |
| Step definitions | 12 |
| Step-executable scenarios | 3/45 |

## Результат SC-0014-001 Executable Slice

| Item | Было | Стало | Evidence |
| --- | --- | --- | --- |
| `SC-0014-001.step_definitions` | `[]` | `SD-0009..SD-0012` | `StormTelegramCommandExecutableSpecTests` исполняет шаги из `features/storm/st-0014-telegram-bot.feature`. |
| `CV-0003 / AC-0039` | command/auth TUnit evidence | command/auth TUnit evidence + executable step-definition slice | `TS-0022` покрывает handler seam; `TS-0028` покрывает executable BDD path. |

## Оставшиеся Partial AC

Нет.

## Coverage Backlog

| ID | Target | Status | Tests / Minimal tests | Результат |
| --- | --- | --- | --- | --- |
| CV-0001 | AC-0032 / ST-0011 | covered_by_contract_tests | TS-0017 | Auth flow получил passing contract-level BDD evidence. |
| CV-0002 | AC-0033 / ST-0011 | covered_by_live_task_api_and_signalr_tests | TS-0017, TS-0018, TS-0019, TS-0020 | ServiceStack task API и SignalR live paths покрыты. |
| CV-0003 | AC-0039 / ST-0014 | covered_by_telegram_command_auth_tests | TS-0022, TS-0028 | Command/auth покрыты; `SC-0014-001` стал step-executable. |
| CV-0004 | AC-0040 / ST-0014 | covered_by_telegram_callback_and_timer_tests | TS-0023, TS-0025, TS-0027 | Callback behavior и Git timer conflict-safety покрыты; `SC-0014-002` стал step-executable. |
| CV-0005 | AC-0042 / ST-0015 | covered_by_project_contract_tests | TS-0024, TS-0026 + Browser Release build smoke | Browser build smoke подтвержден; `SC-0015-002` стал step-executable; Android/iOS build smoke blocked by `NETSDK1147`; runtime release claim не заявляется. |
| CV-0006 | PRODUCT-ENTRY / ST-0016 | covered_by_product_story_and_existing_ui_test | TS-0021 | Error-toast behavior связан с product story. |
| CV-0007 | PRODUCT-ENTRY / proposed_attachment_workflow | internal_orphan_contract_candidate | no active cover link | Вариант B: attachment code сохранен как internal/orphan candidate. |

## BDD Behavior Coverage

| Метрика | Значение |
| --- | --- |
| Feature files | 16 |
| Gherkin Rules | 44 |
| Gherkin Scenarios | 45 |
| Active stories со scenarios | 16/16 |
| AC со Gherkin rules | 44/44 |
| AC со Gherkin scenarios | 44/44 |
| Automated or passing scenarios | 45 |
| Draft scenarios | 0 |
| Passing scenarios | 7 |
| Failing scenarios | 0 |
| Scenarios with linked tests | 45/45 |
| Step definitions | 12 |
| Step-executable scenarios | 3/45 |
| Executable specification ratio | 3/45 step-executable; 7/45 passing scenarios |

## Validation Evidence

| Проверка | Результат |
| --- | --- |
| `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore` | прошло с существующими warnings |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTelegramCommandExecutableSpecTests/*" --output Detailed` | прошло 1/1 |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCommandAuthorizationTests/*" --output Detailed` | прошло 7/7 |

## Оставшиеся Gaps

1. Step definitions покрывают только `SC-0015-002`, `SC-0014-002` и `SC-0014-001`: остальные scenarios пока rely on linked TUnit evidence.
2. Android/iOS build smoke требует отдельной environment/setup task из-за `NETSDK1147`; runtime smoke и release pipeline evidence не заявлены.
3. `CV-0007` не является active cover gap после Варианта B.

## Рекомендуемый Следующий Шаг

Для продолжения `/storm:cover` активных behavior gaps сейчас нет. Следующий SPEC-кандидат: расширить executable step definitions на следующий high-value scenario или отдельно снять Android/iOS `NETSDK1147` environment blocker.
