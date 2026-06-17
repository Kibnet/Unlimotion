# STORM Coverage Analysis

Сгенерировано: 2026-06-17
Команда: `/storm:cover CV-0005 ST-0015 platform shell policy contract coverage`
Режим: delivery-task после утверждения SPEC; tests и product artifacts изменялись, production runtime code и test annotations не менялись

## Область

Этот отчёт обновляет requirements coverage после добавления `TS-0024` для `ST-0015/AC-0042`. Android, browser и iOS shells закрыты как `project-contract supported` surfaces: проекты существуют, подключают общую Avalonia UI-модель, имеют platform packages/startup hooks, а Android содержит native Git assets. Runtime release maturity, signing, store readiness, install/update success и UX parity не заявляются без отдельного platform validation evidence.

## Сводка

| Метрика | Значение |
| --- | --- |
| Acceptance criteria всего | 44 |
| AC с тестовыми связями | 44 |
| AC с уровнем full/critical | 43 |
| AC с уровнем partial | 1 |
| AC без тестовых связей | 0 |
| AC улучшены текущим delivery evidence | 1 |
| Scenario -> Test links | 44/45 |
| Passing scenarios | 6 |

## Улучшено Текущим Delivery Evidence

| AC | Story | Новый уровень | Tests | Почему |
| --- | --- | --- | --- | --- |
| AC-0042 | ST-0015: Собирать, обновлять и проверять cross-platform application shells | critical | TS-0024 | `PlatformShellProjectContractTests` прошли 3/3 и подтверждают Android/browser/iOS project contracts без platform runtime launch. Уровень `full` не выставлен, потому что release/runtime maturity требует отдельного build/runtime evidence. |

## Оставшиеся Partial AC

| AC | Story | Tests | Причина |
| --- | --- | --- | --- |
| AC-0040 | ST-0014 | TS-0023 | Callback subset покрыт; Git timer/conflict-safety часть требует отдельного product decision или `/storm:bdd-implement`. |

## AC Без Тестовых Связей

Нет.

## Coverage Backlog

| ID | Target | Status | Route | Tests / Minimal tests | Результат / Prerequisite |
| --- | --- | --- | --- | --- | --- |
| CV-0001 | AC-0032 / ST-0011 | covered_by_contract_tests | delivery-task/QUEST | TS-0017 | Auth flow получил passing contract-level BDD evidence. |
| CV-0002 | AC-0033 / ST-0011 | covered_by_live_task_api_and_signalr_tests | delivery-task/QUEST | TS-0017<br>TS-0018<br>TS-0019<br>TS-0020 | ServiceStack task API и SignalR live paths covered. |
| CV-0003 | AC-0039 / ST-0014 | covered_by_telegram_command_auth_tests | delivery-task/QUEST completed | TS-0022 | TS-0022 passed 7/7; command/auth covered. |
| CV-0004 | AC-0040 / ST-0014 | partially_covered_callbacks_timer_gap | delivery-task/QUEST partially completed | TS-0023<br>TelegramBot_GitTimers_DoNotRunWhenConflictResolutionIsInProgress remains implementation gap | TS-0023 passed 7/7; callbacks covered. Timer/conflict-safety part is not current covered behavior and should move to separate `/storm:bdd-implement` if product-supported. |
| CV-0005 | AC-0042 / ST-0015 | covered_by_project_contract_tests | delivery-task/QUEST completed | TS-0024 | Conservative platform policy accepted by SPEC approval; Android/browser/iOS project contracts covered 3/3 without runtime release claim. |
| CV-0006 | PRODUCT-ENTRY / ST-0016 | covered_by_product_story_and_existing_ui_test | artifact-only completed | TS-0021 | Error-toast behavior linked to product story. |
| CV-0007 | PRODUCT-ENTRY / proposed_attachment_workflow | proposed | guided-artifact-workflow_then_delivery-task_if_tests_change | AttachmentService_GetUploadDownload_RoundTripsUserAttachment<br>AttachmentMapping_PreservesAttachmentMetadataAcrossDomainAndApiMolds | Подтвердить, есть ли attachment workflow в актуальном продукте. |

## BDD Behavior Coverage

| Метрика | Значение |
| --- | --- |
| Feature files | 16 |
| Gherkin Rules | 44 |
| Gherkin Scenarios | 45 |
| Active stories со scenarios | 16/16 |
| AC со Gherkin rules | 44/44 |
| AC со Gherkin scenarios | 44/44 |
| Automated or passing scenarios | 44 |
| Draft scenarios | 1 |
| Passing scenarios | 6 |
| Failing scenarios | 0 |
| Scenarios with linked tests | 44/45 |
| Step definitions | 0 |
| Executable specification ratio | 6/45 |

## Validation Evidence

| Проверка | Результат |
| --- | --- |
| `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore` | passed, только существующие warnings |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/PlatformShellProjectContractTests/*" --output Detailed` | passed 3/3 |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/SingleViewStartupUiTests/*" --output Detailed` | passed 4/4 |
| `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug --no-restore` | optional smoke blocked by `NETSDK1147` / workload restore state; environment not repaired in this task |
| `dotnet build src/Unlimotion.Browser/Unlimotion.Browser.csproj -c Release --no-restore` | optional smoke blocked by missing `project.assets.json` under `--no-restore`; restore not performed in this task |

## Открытые Вопросы

1. Нужно ли реализовать TelegramBot Git timer conflict-safety как отдельный `/storm:bdd-implement` task?
2. Нужен ли отдельный `/storm:bdd-implement` или platform validation task для реального Android/browser/iOS runtime smoke/release pipeline evidence?
3. Есть ли attachment workflow в актуальном продукте?

## Рекомендуемый Следующий Шаг

Для продолжения `/storm:cover` следующий ranked uncovered item - `CV-0007` attachment workflow confirmation. Если вместо coverage-analysis нужен engineering delivery, открыть отдельную SPEC для `ST-0014` Git timer conflict-safety или для platform runtime smoke.
