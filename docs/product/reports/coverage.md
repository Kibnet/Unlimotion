# STORM Coverage Analysis

Сгенерировано: 2026-06-18
Команда: `/storm:platform-runtime-validation ST-0015`
Режим: artifact-only validation после подтвержденной SPEC; production code, tests и test annotations не менялись

## Область

Эта итерация актуализирует platform evidence для `ST-0015 / AC-0042`: Browser Release build smoke прошел, Android/iOS build smoke заблокированы локальным `NETSDK1147` workload restore state.

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

## Результат ST-0015 Platform Validation

| Item | Было | Стало | Evidence |
| --- | --- | --- | --- |
| CV-0005 / AC-0042 | `covered_by_project_contract_tests` без build smoke | `covered_by_project_contract_tests` + Browser Release build smoke | `TS-0024` покрывает project contracts; `dotnet build src/Unlimotion.Browser/Unlimotion.Browser.csproj -c Release` прошел. |
| Android/iOS smoke | не проверено в текущих artifacts | blocked by environment/workload restore state | `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug` и `dotnet build src/Unlimotion.iOS/Unlimotion.iOS.csproj -c Debug` остановлены `NETSDK1147`. |

## Оставшиеся Partial AC

Нет.

## Coverage Backlog

| ID | Target | Status | Tests / Minimal tests | Результат |
| --- | --- | --- | --- | --- |
| CV-0001 | AC-0032 / ST-0011 | covered_by_contract_tests | TS-0017 | Auth flow получил passing contract-level BDD evidence. |
| CV-0002 | AC-0033 / ST-0011 | covered_by_live_task_api_and_signalr_tests | TS-0017, TS-0018, TS-0019, TS-0020 | ServiceStack task API и SignalR live paths покрыты. |
| CV-0003 | AC-0039 / ST-0014 | covered_by_telegram_command_auth_tests | TS-0022 | Command/auth покрыты. |
| CV-0004 | AC-0040 / ST-0014 | covered_by_telegram_callback_and_timer_tests | TS-0023, TS-0025 | Callback behavior и Git timer conflict-safety покрыты. |
| CV-0005 | AC-0042 / ST-0015 | covered_by_project_contract_tests | TS-0024 + Browser Release build smoke | Browser build smoke подтвержден; Android/iOS build smoke blocked by `NETSDK1147`; runtime release claim не заявляется. |
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
| Step definitions | 0 |
| Executable specification ratio | 7/45 |

## Validation Evidence

| Проверка | Результат |
| --- | --- |
| `dotnet workload list` | прошло; installed workloads include `android`, `ios`, `maccatalyst`, `maui-windows`, `wasm-tools` |
| `dotnet --info` | прошло; SDK `10.0.301`, Workload version `10.0.300-manifests.6fc1bb7b`, Host `11.0.0-preview.4.26230.115` |
| `dotnet build src/Unlimotion.Browser/Unlimotion.Browser.csproj -c Release` | прошло с существующими warnings; output `src/Unlimotion.Browser/bin/Release/net10.0-browser/Unlimotion.Browser.dll` |
| `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug` | blocked by `NETSDK1147`; suggested `dotnet workload restore` for `wasm-tools` |
| `dotnet build src/Unlimotion.iOS/Unlimotion.iOS.csproj -c Debug` | blocked by `NETSDK1147`; suggested `dotnet workload restore` for `wasm-tools` |

## Оставшиеся Gaps

1. `step_definitions` остаются пустыми: текущий BDD layer связан с TUnit tests напрямую, без executable Gherkin runner.
2. Android/iOS build smoke требует отдельной environment/setup task из-за `NETSDK1147`; runtime smoke и release pipeline evidence не заявлены.
3. `CV-0007` не является active cover gap после Варианта B.

## Рекомендуемый Следующий Шаг

Для продолжения `/storm:cover` активных behavior gaps сейчас нет. Следующий осмысленный SPEC-кандидат: environment/setup task для Android/iOS workload restore blocker или отдельный `/storm:bdd-implement` для executable Gherkin step definitions, если процессу нужен настоящий Gherkin runner.
