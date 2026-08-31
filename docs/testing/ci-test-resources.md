# Общие ресурсы тестов

Ресурсы относятся к одному процессу тестов. Отдельные Main/Headless процессы не разделяют Avalonia statics. `--maximum-parallel-tests 1` не принимается за доказательство отсутствия параллельных test bodies: проверяется фактическая трасса установленного TUnit 1.44.0.

| Ресурс | Владельцы / вызывающие тесты | Действующее ограничение | Диагностика |
| --- | --- | --- | --- |
| Avalonia application, dispatcher, styles, locator, popup reference | UI test classes с `AvaloniaHeadless`; `MainWindowViewModelTests`, `TaskStatusTransitionTests`, другие владельцы прямых sessions | `NotInParallel("AvaloniaHeadless")` и `SharedUiStateParallelLimit` у соответствующих entry points | `scheduler-scope` охватывает test hook interval; отдельные Safe lifetime и dispatch leases уточняют операции |
| ReactiveUI scheduler и DefaultThrottleTime | `ReactiveUiSessionHooks` и `BaseModelTests`/UI fixtures | Инициализация once per test session; BaseModel entry points используют общий UI limiter | lifecycle + объявленные scheduler constraints; session initialization не является параллельным test body |
| ServiceStack global host/JWT provider, embedded RavenDB | `ServerStorageCrudRealtimeContract`, canonical `StormServerStorageCrudRealtimeExecutableSpecTests` | `NotInParallel("ServerStorageLiveIntegration")`; fixture на каждый live subcase | scheduler scope, live setup/body-and-client-cleanup/host-cleanup phases |
| Настройки и persisted fault cases | `TaskSpaceTransactionTests` | Новый GUID directory на fixture, отдельный physical JSON/config instance на каждый case; seed эксперимент откатан без сокращения матрицы | case ID, recorded write paths, setup/write/recovery, executed/passed indices |
| Пробный общий ресурс | `TestParallelismProbeTests` (2 параметра) | `ProbeSharedResource` + общий limiter | active counter assertion и resource leases |
| Пробный независимый ресурс | `IndependentTestParallelismProbeTests` | Отдельный constraint key | Фактическое исполнение; overlap не обязателен и не asserted |
| Avalonia в отдельном `Unlimotion.UiTests.Headless` процессе | `MainWindowHeadlessTests`, оба `ReadmeDemo*HeadlessTests`, Settings/TaskSpaces/StartupRecovery/StorageLifecycle и наследуемые authoring cases | `NotInParallel("DesktopUi")`, включая method attributes наследуемых scenarios | TUnit HTML test-body/test-case spans; Main diagnostic hooks в эту сборку не подключены |

Статические UI contracts сами не являются TUnit tests. Владельцы следующих прямых sessions — соответствующие BDD entry points с `AvaloniaHeadless` и `SharedUiStateParallelLimit`:

| Helper | Entry point |
| --- | --- |
| `FilterResetUiContract` | `StormFilterResetExecutableSpecTests` |
| `TaskCardLayoutUiContract` | `StormTaskCardLayoutExecutableSpecTests` |
| `TaskPlanningDatesUiContract` | `StormTaskPlanningDatesExecutableSpecTests` |
| `ToastNotificationUiContract` | `StormNotificationToastExecutableSpecTests` |
| `WorkspaceTreeCommandsUiContract` | `StormWorkspaceTreeCommandsExecutableSpecTests` |
| `WantedImportanceUiContract` | `StormTaskPlanningWantedImportanceExecutableSpecTests` |
| `WorkspaceBreadcrumbsLastOpenedUiContract` | `StormWorkspaceBreadcrumbsLastOpenedExecutableSpecTests` |
| `WorkspaceNavigationTabsUiContract` | `StormWorkspaceNavigationTabsExecutableSpecTests` |

`HeadlessSessionExtensions` — общий helper, а `TestHelpers` читает throttle для ожидания и не является дополнительным владельцем UI state. Диагностика ограничений использует method/class/assembly attributes с приоритетом method → class → assembly; generic limiters записываются отдельно. Она не добавляет новых блокировок и не меняет scheduling.

При анализе проверить парность entered/left по process + execution ID (или lease ID), отсутствие overlap одинакового scheduler constraint/resource, а также пропущенные lifecycle events. Незакрытый lease, отсутствующая metadata или ошибка trace — incomplete evidence, не нулевой overlap.

Финальная локальная проверка31.08.2026: три Main executions по906 lifecycle/body/case spans complete, без open leases и overlap одинакового declared resource; независимые bodies пересекались, максимум4. Три Headless executions имеют по38 body/case spans и concurrency1. Exact paths/hashes — `candidate-full-correctness-v1/validation-summary.json` внутри локальных artifacts. Это evidence ограничений ресурсов в указанных процессах, не гарантия отсутствия всех background races или worker drain после known Safe-session suppression.
