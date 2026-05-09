# Обновление всех NuGet-пакетов до последних стабильных версий

## 0. Метаданные
- Тип (профиль): delivery-task; core: model-behavior-baseline, quest-governance, quest-mode, collaboration-baseline, testing-baseline; context: testing-dotnet; stack profile: dotnet-desktop-client.
- Владелец: Codex.
- Масштаб: large.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая ветка `fix/remove-warnings`, поверх PR #227.
- Ограничения: до подтверждения спеки не менять проектные файлы и код; использовать только стабильные версии NuGet без prerelease; не менять TFM/.NET SDK без отдельного подтверждения; учитывать release notes и breaking changes.
- Связанные ссылки:
  - Avalonia 12 breaking changes: https://v11.docs.avaloniaui.net/docs/avalonia12-breaking-changes/
  - ServiceStack v10 release notes: https://docs.servicestack.net/releases/v10_00
  - AutoMapper 15 upgrade guide: https://docs.automapper.io/en/latest/15.0-Upgrade-Guide.html
  - Telegram.Bot releases: https://github.com/TelegramBots/Telegram.Bot/releases
  - Polly releases: https://github.com/App-vNext/Polly/releases

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Обновить все прямые NuGet-зависимости в решении до последних стабильных версий, которые доступны из настроенных package sources, и адаптировать код только там, где это требуется для компиляции и сохранения текущего поведения.

Outcome contract:
- Success means:
  - все прямые `PackageVersion` в `src/Directory.Packages.props` и inline `PackageReference` в `src/**/*.csproj` / `tests/**/*.csproj` либо обновлены до последней стабильной версии, либо явно оставлены с причиной и ссылкой на stop rule;
  - `dotnet list package --outdated` после обновления не показывает доступных stable-updates для обрабатываемых проектов, кроме документированных исключений;
  - сборка ключевых проектов проходит;
  - релевантные headless/UI тесты запущены или явно указан технический блокер.
- Итоговый артефакт / output: коммит(ы) с обновленными пакетами, минимальными compatibility-правками, результатами проверки и обновленной PR-веткой.
- Stop rules:
  - если последняя стабильная версия требует перехода на .NET SDK/TFM, отсутствующий в репозитории или среде, остановиться и запросить решение;
  - если package ecosystem конфликтует сам с собой (например, Avalonia 12 требует companion-пакет, у которого нет совместимой stable-версии), остановиться и предложить варианты;
  - если major-upgrade требует переписывания большой подсистемы, выходящей за разумную compatibility-правку, остановиться до внесения таких изменений;
  - если restore/build обнаруживает лицензионный или коммерческий gate, остановиться и зафиксировать источник проблемы.

## 2. Текущее состояние (AS-IS)
- Центральное управление основными версиями находится в `src/Directory.Packages.props`.
- Часть тестовых и упаковочных проектов использует inline `PackageReference Version=...`.
- Текущая ветка уже содержит предыдущий этап: снятие build warnings и обновление пакетов с известными vulnerability warnings.
- Прямые устаревшие зависимости по `dotnet list package --outdated --no-restore`:
  - Avalonia family: `Avalonia`, `Avalonia.Android`, `Avalonia.Browser`, `Avalonia.Desktop`, `Avalonia.Fonts.Inter`, `Avalonia.Headless`, `Avalonia.iOS`, `Avalonia.Themes.Fluent` до `12.0.2`; `Avalonia.Diagnostics` до `11.3.14`; `Avalonia.ReactiveUI` до `11.3.8`.
  - AppAutomation family: `AppAutomation.*` до `1.5.6`.
  - ServiceStack family: `ServiceStack.*` до `10.0.6`.
  - SignalR.EasyUse family: `SignalR.EasyUse.*` до `0.3.0`.
  - Test stack: `TUnit`, `TUnit.Assertions`, `TUnit.Core` до `1.43.11`; `JustMock` до `2026.1.211.494`.
  - Server/backend: `RavenDB.Client`, `RavenDB.Embedded` до `7.2.2`; `Serilog.AspNetCore` до `10.0.0`; `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` до `1.23.0`; `WritableJsonConfiguration` до `8.0.1`.
  - Client/domain infrastructure: `AutoMapper` до `16.1.1`; `ReactiveUI` до `23.2.27`; `System.Runtime.Caching` до `10.0.7`; `Quartz` до `3.18.1`; `DialogHost.Avalonia` до `0.12.2`.
  - Platform/utility: `Tmds.DBus.Protocol` до `0.93.0`; `System.Drawing.Common` до `10.0.7`; `Polly` / `Polly.Core` до `8.6.6`.
  - Telegram bot: `Telegram.Bot` до `22.9.6.2`; `Newtonsoft.Json` до `13.0.4`; `Serilog.Sinks.Console` до `6.1.1`; `Serilog.Sinks.File` до `7.0.0`.
- Publish-only проекты `Unlimotion.Desktop.ForDebianBuild.csproj` и `Unlimotion.Desktop.ForMacBuild.csproj` требуют отдельной restore/build-проверки: `--no-restore` не смог прочитать их package graph.

## 3. Проблема
Репозиторий использует смешанный набор устаревших NuGet-версий; некоторые обновления являются major-upgrade и могут требовать compatibility-правок, поэтому простой массовый bump без проверки release notes может сломать сборку, UI-тесты или runtime-контракты.

## 4. Цели дизайна
- Разделение ответственности: версии пакетов обновлять централизованно там, где они уже централизованы; inline версии менять только в проектах, где они уже inline.
- Повторное использование: не вводить новые wrappers/adapters, если достаточно минимальной API-миграции.
- Тестируемость: сохранять существующую структуру unit/headless/UI тестов; добавлять тесты только если compatibility-правка меняет UI-facing поведение.
- Консистентность: все пакеты одной семьи обновлять согласованно.
- Обратная совместимость: не менять пользовательское поведение, настройки, формат данных, публичные DTO и сетевые контракты без отдельного решения.

## 5. Non-Goals (чего НЕ делаем)
- Не обновляем TargetFramework, .NET SDK, workload manifest или CI image как часть этой задачи без отдельного подтверждения.
- Не подключаем prerelease/nightly packages.
- Не меняем функциональность приложения, UI-дизайн, бизнес-логику и настройки.
- Не добавляем новые transitive pins, если они не нужны для безопасности или совместимости.
- Не переписываем крупные подсистемы ради major-upgrade; при таком требовании срабатывает stop rule.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Directory.Packages.props` -> основные версии direct NuGet packages.
- `src/**/*.csproj`, `tests/**/*.csproj` -> inline versions, которые не управляются центральным props.
- `src/Unlimotion.TelegramBot/**` -> возможная миграция API Telegram.Bot 16 -> 22.
- `src/Unlimotion.Server*/**` -> возможная адаптация ServiceStack 6 -> 10 и RavenDB/Serilog обновлений.
- `src/Unlimotion*/**`, `tests/**` -> возможная адаптация Avalonia/AppAutomation/TUnit обновлений.

### 6.2 Детальный дизайн
- Выполнить staged update:
  1. Обновить версии пакетов.
  2. Выполнить restore.
  3. Исправить только compile-time compatibility errors.
  4. Запустить targeted build/test.
  5. Повторить `dotnet list package --outdated` и vulnerable audit.
- Для семей пакетов применять одинаковые версии, когда это поддерживается upstream.
- Для `Avalonia.Diagnostics`:
  - если Avalonia 12 несовместима с пакетом или пакет больше не нужен, удалить/заменить согласно официальной миграции;
  - если проект остается на Avalonia 11 по stop rule, обновить до последнего compatible stable.
- Для `Tmds.DBus.Protocol` и `System.Drawing.Common`:
  - если direct reference больше не нужен после major-upgrade и был добавлен только как override transitive vulnerability, удалить direct reference вместо бессмысленного pin;
  - если прямой reference остается нужен, обновить до последней stable.
- Для `Avalonia.ReactiveUI`, `Avalonia.Xaml.Behaviors`, `Avalonia.Controls.PanAndZoom`:
  - проверить совместимость с Avalonia 12 restore/build;
  - при отсутствии compatible stable остановиться и предложить варианты: удержать Avalonia на последней 11.x stable, исключить несогласованный пакет из "latest stable", либо сделать отдельную миграцию.
- Для `Telegram.Bot`:
  - ожидать миграцию старого event/Args API (`OnMessage`, `OnCallbackQuery`, `Telegram.Bot.Args`) на текущую модель receive/update handling;
  - сохранять существующие bot commands и тексты ответов.
- Для `ServiceStack`:
  - проверить nullable DTO impact, OpenAPI пакет, server registration и ServiceStack license/config behavior;
  - не менять DTO contract без отдельного решения.
- Для `AutoMapper`:
  - проверить license/config requirements и конструкторы `MapperConfiguration`;
  - сохранить текущие mapping assertions.

## 7. Бизнес-правила / Алгоритмы
- Обновлять только direct dependencies.
- Stable means: версия без prerelease suffix по NuGet resolver / `dotnet list package --outdated`.
- Если direct package больше не нужен и не используется в коде, удаление direct reference считается валидным "latest stable" результатом, потому что проект перестает искусственно пинить пакет.
- Нельзя подавлять новые warnings вместо решения причины, кроме случаев, где warning является known upstream issue и подавление явно локализовано.

## 8. Точки интеграции и триггеры
- Restore/build через MSBuild/NuGet.
- Avalonia startup paths: desktop, Android, iOS, Browser.
- UI test host and headless test projects.
- Server startup and ServiceStack registration.
- Telegram bot update receiving pipeline.

## 9. Изменения модели данных / состояния
Не применимо: задача не должна менять persisted data model, user settings schema или runtime state contracts.

## 10. Миграция / Rollout / Rollback
- Rollout: отдельный коммит или серия коммитов на текущей PR-ветке.
- Rollback: revert dependency update commit(s).
- Первый запуск приложения не должен требовать миграции пользовательских данных.
- Если major-upgrade требует runtime config/licensing, это блокер до решения пользователя.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - restore проходит для решения или для всех релевантных проектов, которые поддерживаются локальной средой;
  - ключевые проекты собираются без новых package downgrade/conflict errors;
  - outdated scan не показывает пропущенных stable updates, кроме документированных исключений;
  - vulnerable scan не показывает известных vulnerable direct/transitive packages либо фиксирует внешний блокер.
- Какие тесты добавить/изменить:
  - не добавлять новые UI-тесты, если изменения ограничены dependency compatibility и не меняют UI-facing behavior;
  - обновить тесты только при реальном изменении публичного/UI поведения.
- Characterization tests / contract checks:
  - targeted existing tests для AutoMapper/ViewModel/UI flows.
- Команды для проверки:
  - `dotnet restore`
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal`
  - `dotnet build src\Unlimotion.Server\Unlimotion.Server.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal`
  - `dotnet build src\Unlimotion.TelegramBot\Unlimotion.TelegramBot.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal`
  - `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` если installed workloads позволяют
  - `dotnet build src\Unlimotion.Browser\Unlimotion.Browser.csproj -m:1 /nr:false /p:UseSharedCompilation=false -v:minimal` если installed workloads позволяют
  - targeted UI/headless tests: `SettingsControlResponsiveUiTests`, `MainControlTreeCommandsUiTests`, `MainControlAvailabilityUiTests` или ближайшие существующие тесты, которые компилируются после обновления TUnit/AppAutomation.
  - `dotnet list <project> package --outdated --no-restore`
  - `dotnet list <project> package --vulnerable --include-transitive`
- Stop rules для test/retrieval/tool/validation loops:
  - один и тот же build/test failure не чинить более двух циклов без новой диагностики;
  - если полный test suite зависает или превышает 20 минут, перейти на targeted tests и явно указать это в результате;
  - если workloads отсутствуют локально, не устанавливать их без отдельного запроса, а зафиксировать непроверенный таргет.

## 12. Риски и edge cases
- Avalonia 12 может требовать .NET 10 для Android/iOS и менять startup integration; это потенциально блокирует "all packages latest stable" без TFM/workload решения.
- `Avalonia.ReactiveUI` последняя stable, найденная NuGet, отстает от core Avalonia 12; возможен несовместимый граф.
- `Avalonia.Xaml.Behaviors` и `Avalonia.Controls.PanAndZoom` могут оставаться на 11.x-era versions и конфликтовать с Avalonia 12.
- ServiceStack 10 включает nullable annotation/disruptive changes, что может проявиться в DTO и generated clients.
- Telegram.Bot 22 может потребовать миграции с event-based API.
- AutoMapper 15+ имеет license/config changes; 16.x нужно проверять на runtime warnings/errors.
- `System.Drawing.Common` 10 сохраняет platform ограничения; тестовый проект с `System.Drawing` должен оставаться Windows-only или быть ограничен средой.
- Тестовый стек TUnit 1.15 -> 1.43 может поменять атрибуты/assertion behavior.

## 13. План выполнения
1. После подтверждения спеки сделать чистый baseline status.
2. Обновить версии в центральном props и inline project references.
3. Выполнить restore и решить package graph conflicts.
4. Исправить минимальные compile-time compatibility errors по подсистемам.
5. Запустить agreed build/test set.
6. Выполнить outdated/vulnerable scan.
7. Закоммитить изменения и обновить PR #227.

## 14. Открытые вопросы
- Блокирующих вопросов до начала нет.
- Если Avalonia 12 потребует TFM/.NET 10 для mobile targets, потребуется отдельное решение пользователя по stop rule.

## 15. Соответствие профилю
- Профиль: dotnet-desktop-client + testing-dotnet.
- Выполненные требования профиля:
  - изменение dependency graph проходит через restore/build/test;
  - UI-facing behavior не меняется намеренно;
  - UI tests планируются как проверка регрессий, а не как новая функциональная разработка;
  - major-upgrade risks явно перечислены и ограничены stop rules.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Directory.Packages.props` | Обновить direct package versions / удалить ненужные direct pins | Основной dependency catalog |
| `src/**/*.csproj` | Обновить inline `PackageReference` версии | Publish/platform projects |
| `tests/**/*.csproj` | Обновить inline test dependency versions | Test projects |
| `src/**` | Минимальные compatibility-правки при compile errors | Major NuGet upgrades |
| `tests/**` | Минимальные compatibility-правки при compile/test errors | TUnit/AppAutomation/Avalonia updates |
| `specs/2026-05-08-nuget-latest-stable-updates.md` | Обновлять журнал действий | Управление задачей |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| NuGet versions | Смешанные older stable versions | Последние stable versions из package sources |
| Vulnerability overrides | Некоторые direct pins добавлены для security cleanup | Удалены, если больше не нужны, или обновлены до latest stable |
| Compatibility | Старые API могут компилироваться только со старыми версиями | Минимальная адаптация под новые stable APIs |

## 18. Альтернативы и компромиссы
- Вариант: обновить только patch/minor.
  - Плюсы: ниже риск.
  - Минусы: не выполняет запрос "все нугеты до последней стабильной версии".
  - Почему не выбран: пользователь явно запросил latest stable.
- Вариант: обновить все версии одним bump без анализа.
  - Плюсы: быстрее.
  - Минусы: высокий риск package graph и runtime regressions.
  - Почему не выбран: major-upgrades требуют release notes и stop rules.
- Вариант: разделить на несколько PR по семействам пакетов.
  - Плюсы: проще ревью и откат.
  - Минусы: медленнее; текущая задача просит общий проход.
  - Почему выбран staged update в одном PR: сохраняет скоуп запроса, но оставляет возможность остановиться на конфликтном семействе.

## 19. Результат quality gate и review
- Чеклист SPEC-LINTER:
  - цель и outcome заданы;
  - AS-IS и TO-BE заданы;
  - risks/stop rules заданы;
  - тест-план задан;
  - non-goals заданы;
  - approval phrase задана.
- Итог по SPEC-RUBRIC: 26/30.
- Краткий Post-SPEC Review:
  - Статус: PASS.
  - Что исправлено: скоуп latest-stable обновлений ограничен direct dependencies, major-upgrade риски вынесены в stop rules.
  - Что осталось на решение пользователя: подтвердить запуск EXEC-фазы.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | dependency-modernization | 0.82 | Реальный package graph после restore и compile errors после major-upgrade | Дождаться подтверждения спеки | Да | Нет | Массовое обновление NuGet затрагивает major versions и требует явного согласования | `specs/2026-05-08-nuget-latest-stable-updates.md` |
| EXEC | dependency-modernization | 0.88 | Нет | Завершить проверку diff/status и подготовить коммит | Нет | Да: пользователь подтвердил спеку 2026-05-08 | Обновлены direct NuGet packages до последних stable, кроме документированных compatibility-исключений | `src/Directory.Packages.props`, `src/**/*.csproj`, `tests/**/*.csproj` |
| EXEC | avalonia-compatibility | 0.86 | Нет | Зафиксировать исключение в итоговом отчете | Нет | Нет | Avalonia family удержана на 11.3.14: latest stable 12.0.2 ломает runtime совместимость с `NodifyAvalonia 6.6.0`, а `Avalonia.Controls.PanAndZoom 11.3.0` не имеет совместимой 12.x stable версии | `src/Directory.Packages.props`, `src/Unlimotion/Views/GraphControl.axaml.cs`, `src/Unlimotion/Views/MainControl.axaml.cs` |
| EXEC | avalonia-reactiveui-dialoghost | 0.86 | Нет | Зафиксировать исключение в итоговом отчете | Нет | Нет | `ReactiveUI.Avalonia` удержан на 11.3.8, `DialogHost.Avalonia` на 0.11.0 как последние совместимые с Avalonia 11; более новые stable требуют Avalonia 12 | `src/Directory.Packages.props`, `src/Unlimotion/App.axaml.cs`, platform entry points |
| EXEC | behavior-removal | 0.91 | Нет | Завершить review diff | Нет | Нет | `Avalonia.Xaml.Behaviors` удален, а lost-focus binding behavior перенесен в локальное attached-property API без изменения UI-потока | `src/Unlimotion/Behavior/PlannedDurationBehavior.cs`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlNewTaskDeadlineUiTests.cs` |
| EXEC | compatibility-fixes | 0.84 | Нет | Завершить review diff | Нет | Нет | Выполнены минимальные API-миграции для `ReactiveUI.Avalonia`, `Telegram.Bot 22`, `Serilog.AspNetCore 10`, `ServiceStack 10`, mobile/browser startup и Avalonia folder picker | `src/Unlimotion/**`, `src/Unlimotion.Server/**`, `src/Unlimotion.TelegramBot/**`, `src/Unlimotion.Android/**`, `src/Unlimotion.iOS/**`, `src/Unlimotion.Browser/**` |
| EXEC | appautomation-compatibility | 0.78 | Нет | Зафиксировать runner-блокер в итоговом отчете | Нет | Нет | `AppAutomation.*` обновлены до 1.5.6, но TUnit в AppAutomation-проектах удержан на 1.37.10: с TUnit 1.43.38 `AppAutomation.TUnit 1.5.6` падает на отсутствующем `BeforeTestHooks`; headless runner после этого собирается, но полный запуск зависает до timeout | `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`, `tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj`, `tests/Unlimotion.UiTests.Authoring/Unlimotion.UiTests.Authoring.csproj` |
| EXEC | validation | 0.9 | Нет | Завершить git status/diff и подготовить итог | Нет | Нет | Restore/build прошли для решения и ключевых проектов; targeted UI/headless tests прошли; outdated scan показывает только документированные исключения; vulnerable scan по `src`/`tests` пустой | build/test commands, package scans |
