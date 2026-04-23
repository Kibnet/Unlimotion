# Velopack GitHub Releases Updates

## 0. Метаданные
- Тип (профиль): delivery-task; stack profile `dotnet-desktop-client`; context `testing-dotnet`
- Владелец: Codex
- Масштаб: medium
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения:
  - До подтверждения этой спеки разрешено менять только этот файл.
  - Первая реализация охватывает desktop release channels `win`, `linux` и `osx` в рамках текущих desktop build-проектов.
  - Текущие MSI и portable ZIP release assets не удаляются в рамках этой задачи.
  - Существующие мобильные, browser и shared Avalonia проекты не должны получить обязательную зависимость от Velopack.
- Связанные ссылки:
  - https://docs.velopack.io/integrating/overview
  - https://docs.velopack.io/reference/cs/Velopack/Sources/GithubSource
  - https://www.nuget.org/packages/Velopack
  - https://www.nuget.org/packages/vpk

## 1. Overview / Цель
Добавить основу автообновления desktop-приложения через Velopack и GitHub Releases: приложение должно корректно обрабатывать Velopack install/update hooks, уметь проверять релизные артефакты GitHub Releases, а release workflows должны публиковать Velopack-пакеты и feed metadata для `win`, `linux` и `osx` вместе с текущими артефактами.

## 2. Текущее состояние (AS-IS)
- Desktop-приложение находится в `src/Unlimotion.Desktop` и запускает shared Avalonia app из `src/Unlimotion`.
- `src/Unlimotion.Desktop/Program.cs` выполняет раннюю настройку путей, затем вызывает `App.Init(configPath)` и `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`.
- `src/Directory.Packages.props` использует centralized package management.
- `.github/workflows/msi_packaging.yml` запускается на `release.published`, публикует `dotnet publish` output для `win-x64`, собирает MSI через Advanced Installer и загружает MSI + portable ZIP в GitHub Release.
- `.github/workflows/deb_packaging.yml` и `.github/workflows/osx-packaging.yml` уже публикуют Linux/macOS release assets (`.deb` и `.pkg`), но не создают Velopack-managed update feeds/packages.
- `Unlimotion.aip` остается текущей схемой MSI packaging.
- Пользовательские данные и настройки в Release режиме пишутся вне папки приложения: `Settings.json` в `Documents/Unlimotion`, задачи через `TaskStorageFactory`, backup paths через `LocalApplicationData/Unlimotion`. Это важно, потому что Velopack заменяет папку установленного приложения при обновлении.
- `src/Unlimotion/Views/SettingsControl.axaml` уже содержит секционную страницу настроек: внешний вид, хранилище, backup, advanced backup и service actions.
- `SettingsViewModel` живет в `src/Unlimotion.ViewModel`, а команды для действий настроек в основном назначаются из `src/Unlimotion/App.axaml.cs`.
- Готового механизма проверки обновлений в приложении нет.

## 3. Проблема
У приложения нет встроенного канала автообновления: GitHub Release assets публикуются, но установленное desktop-приложение не проверяет новые версии, не скачивает update package и не применяет обновление.

## 4. Цели дизайна
- Разделение ответственности: Velopack-зависимость и GitHub update source остаются в desktop entrypoint/desktop services.
- Повторное использование: release workflow переиспользует существующий `dotnet publish` output.
- Cross-platform release safety: каждый desktop runtime публикуется в отдельный Velopack channel/feed, чтобы Windows/Linux/macOS не пересекались в одном release index.
- Тестируемость: логика решения "есть обновление -> скачать -> спросить о рестарте / не падать при ошибке" отделяется от прямых Velopack API через тонкую абстракцию.
- Консистентность: текущие настройки, данные задач и backup-настройки не переносятся в папку приложения.
- Обратная совместимость: MSI и portable ZIP продолжают публиковаться, пока Velopack-путь не будет проверен на реальных релизах.

## 5. Non-Goals (чего НЕ делаем)
- Не удаляем Advanced Installer/MSI workflow.
- Не переводим Android, iOS и Browser проекты на Velopack.
- Не добавляем self-hosted update server, S3, Azure Storage или отдельный backend.
- Не меняем формат пользовательских данных и не переносим существующие настройки.
- Не делаем downgrade/channel switching UI.
- Не вводим обязательный GitHub personal access token для публичных релизов.
- Не добавляем настройки автоматической периодичности обновлений, каналов и prerelease-потоков в первой итерации.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Directory.Packages.props` -> добавить стабильную версию NuGet-пакета `Velopack` и dotnet tool package `vpk` при необходимости фиксации через tool manifest.
- `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj` -> добавить `PackageReference Include="Velopack"`.
- `src/Unlimotion.Desktop/Program.cs` -> вызвать `VelopackApp.Build().Run()` максимально рано в `Main`, до пользовательской и Avalonia-инициализации.
- `src/Unlimotion.ViewModel/*` -> добавить platform-neutral интерфейс update service и состояние/команды `SettingsViewModel` для ручной проверки, скачивания и применения обновлений.
- `src/Unlimotion.Desktop/Services/*` -> добавить desktop-only Velopack implementation для platform-neutral update service.
- `src/Unlimotion/App.axaml.cs` -> подключить update service к `SettingsViewModel` после создания view model, без прямой зависимости shared проекта от Velopack.
- `src/Unlimotion/Views/SettingsControl.axaml` -> добавить секцию "Обновления" с текущим статусом, ручной проверкой и применением скачанного обновления.
- `.github/workflows/msi_packaging.yml`, `.github/workflows/deb_packaging.yml`, `.github/workflows/osx-packaging.yml` -> добавить шаги установки `vpk`, упаковки platform-specific publish output и загрузки Velopack assets/feed metadata в текущий GitHub Release.
- `src/Unlimotion.Test` -> добавить unit tests для coordinator logic, если абстракция будет жить в тестируемом shared/desktop-neutral слое; иначе ограничиться build/packaging smoke checks для инфраструктурной части.

### 6.2 Детальный дизайн
- На старте `Program.Main` первым значимым действием вызывает Velopack startup hook:
  - `VelopackApp.Build().Run();`
  - После FastCallback приложение должно быстро выйти, если Velopack запустил его с install/update аргументами.
- Проверка обновлений запускается после открытия главного окна, чтобы не блокировать UI startup.
- Update source: `new UpdateManager(new GithubSource("https://github.com/<owner>/<repo>"))`.
- Отдельные feed URLs/репозитории не нужны: Velopack сам использует `releases.{channel}.json` в том же GitHub Release asset set. Для cross-platform rollout каналы должны быть раздельными (`win`, `linux`, `osx`) или явно заданными platform-specific значениями.
- Репозиторий должен определяться константой или build property для текущего проекта. Если origin невозможно надежно вывести из runtime, использовать явное значение GitHub repo из workflow.
- Поток обновления:
  1. Проверить, запущено ли приложение из Velopack-installed context; portable/MSI legacy запуск не должен падать.
  2. Асинхронно вызвать `CheckForUpdatesAsync`.
  3. Если обновления нет, завершить без UI шума.
  4. Если обновление есть, скачать через `DownloadUpdatesAsync`.
  5. После скачивания показать существующий `INotificationManagerWrapper.Ask` с предложением перезапустить приложение.
  6. При согласии пользователя вызвать `ApplyUpdatesAndRestart`.
  7. При отказе не прерывать работу; обновление применится позже через явный restart или следующий запуск, если Velopack это поддержит для downloaded pending update.
- Раздел настроек "Обновления":
  - располагается отдельной секцией рядом с существующими секциями `SettingsControl`;
  - показывает текущую версию приложения, статус проверки и, если найдено обновление, доступную версию;
  - содержит кнопку ручной проверки обновлений;
  - содержит кнопку скачивания/подготовки обновления, когда обновление найдено;
  - содержит кнопку применения обновления с перезапуском, когда пакет уже скачан;
  - кнопки должны отключаться на время проверки/скачивания, чтобы пользователь не запускал параллельные update операции;
  - если приложение запущено не из Velopack-managed install, секция должна показывать понятный статус "обновления недоступны для этой установки" или скрывать кнопки применения, но не должна ломать настройки.
- Состояния update UI:
  - `Unsupported`: установка не управляется Velopack или платформа не поддерживается.
  - `Idle`: проверка не выполнялась или готова к запуску.
  - `Checking`: идет проверка GitHub Releases.
  - `NoUpdates`: новых версий нет.
  - `UpdateAvailable`: новая версия найдена, пакет еще не скачан.
  - `Downloading`: идет скачивание update package.
  - `ReadyToApply`: пакет скачан, можно применить с рестартом.
  - `Applying`: запущено применение обновления и рестарт.
  - `Error`: последняя операция завершилась ошибкой; текст ошибки отображается в статусе без падения приложения.
- Ошибки сети, GitHub API, отсутствия Velopack metadata и невалидного release feed логируются/показываются как non-blocking toast только в случаях, когда это не будет раздражать пользователя при каждом запуске. Для первой итерации предпочтительно логировать и не показывать ошибку, кроме уже скачанного обновления, которое не удалось применить.
- Производительность: проверка и скачивание выполняются async/background; UI thread используется только для сообщения пользователю.

## 7. Бизнес-правила / Алгоритмы
- Автообновление не должно принудительно перезапускать приложение без согласия пользователя.
- Ручное применение обновления в настройках всегда явно означает согласие на перезапуск приложения.
- Если обновление найдено фоновой проверкой, пользователь может применить его через prompt или позже через раздел настроек.
- Отсутствие интернет-соединения не должно менять пользовательский сценарий запуска.
- GitHub prerelease не устанавливается в stable channel, если явно не задан отдельный режим.
- Версия релиза должна быть SemVer-compatible для Velopack и `dotnet publish -p:Version=...`.
- Release tag должен совпадать с версией пакета или быть нормализован в workflow до версии, приемлемой для Velopack.

## 8. Точки интеграции и триггеры
- `Program.Main`: Velopack hook до `TaskStorageFactory`, `App.Init` и Avalonia startup.
- `App.Init` или другой platform service bootstrap: desktop entrypoint передает shared app platform-neutral update service; не-desktop entrypoints используют `null`/no-op.
- `App.OnFrameworkInitializationCompleted`: запуск фоновой проверки после создания/открытия desktop window и привязка update commands к `SettingsViewModel`.
- `SettingsControl.axaml`: ручной триггер `CheckForUpdatesCommand`, `DownloadUpdateCommand`, `ApplyUpdateCommand`.
- GitHub Actions `release.published`: упаковка Velopack assets после существующего `dotnet publish`.
- GitHub Release assets: Velopack packages и release metadata должны лежать в том же release, откуда `GithubSource` сможет получить update feed.
- Для cross-platform rollout каждый workflow публикует свой `releases.{channel}.json` и соответствующие platform assets в тот же GitHub release, без общего "универсального" feed-файла.

## 9. Изменения модели данных / состояния
- Новых persisted полей в пользовательских настройках не требуется.
- Update status, available version и downloaded update state являются runtime/calculated state в `SettingsViewModel`, а не persisted settings.
- Новые temporary/update файлы создаются Velopack в собственной install/update структуре.
- Пользовательские данные не должны храниться в Velopack `current` directory.

## 10. Миграция / Rollout / Rollback
- Rollout:
  1. Сначала публиковать Velopack assets вместе с MSI/portable ZIP.
  2. Проверить, что для каждого desktop runtime (`win`, `linux`, `osx`) в release появляются свои Velopack channel assets.
  3. Проверить установку Velopack bootstrapper / portable artifact на отдельных машинах/VM по платформам.
  4. Проверить обновление с версии N на N+1 через GitHub Release хотя бы на одной машине для каждого поддерживаемого desktop runtime.
- Совместимость:
  - Уже установленные MSI-копии не станут Velopack-managed автоматически.
  - Пользователь должен установить Velopack bootstrapper/installer хотя бы один раз, чтобы дальнейшие обновления шли через Velopack.
- Rollback:
  - Отключить/удалить Velopack steps из workflow.
  - Убрать `VelopackApp` hook и desktop update service.
  - MSI/portable ZIP остаются рабочим каналом распространения.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `Program.Main` содержит ранний Velopack hook до пользовательской и Avalonia-инициализации.
  - Desktop проект собирается с пакетом `Velopack`; shared/mobile/browser проекты не получают прямой Velopack dependency.
  - На запуске вне Velopack-installed context приложение не падает.
  - При доступном обновлении coordinator проверяет, скачивает и предлагает рестарт без блокировки UI.
  - В настройках есть секция обновлений с ручной проверкой, скачиванием/подготовкой и применением обновления.
  - В не-Velopack установке раздел настроек показывает unsupported state или отключенные действия без исключений.
  - Во время проверки/скачивания update-кнопки корректно отключаются и статус обновляется.
  - Release workflow публикует Velopack assets в GitHub Release.
  - Release workflows публикуют раздельные Velopack feeds/assets для `win`, `linux` и `osx` в одном GitHub Release.
  - Текущие MSI и portable ZIP assets продолжают публиковаться.
- Какие тесты добавить/изменить:
  - Unit tests для update coordinator/settings update state с fake update client: unsupported install, no update, update available, download success, apply restart, exception path, repeated command guard.
  - Headless UI smoke test для `SettingsControl`, если существующая test infrastructure позволяет проверить наличие секции и enabled/disabled state без хрупких визуальных проверок.
  - Velopack implementation тестировать через build/smoke, без сетевых GitHub вызовов в unit tests.
- Characterization checks:
  - Проверить, что `dotnet publish` command из workflow по-прежнему формирует expected output.
  - Проверить, что `vpk pack` принимает release version из `github.ref_name` или нормализованное значение.
- Команды для проверки:
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`
  - `dotnet publish src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release -f net10.0 -r win-x64 -p:PublishSingleFile=true --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:Version=0.0.0-test --ignore-failed-sources`
  - `vpk pack` smoke command against the publish output, with the exact arguments implemented in workflow.

## 12. Риски и edge cases
- Velopack-managed install и текущий MSI install являются разными install channels; автоматическая миграция MSI -> Velopack не входит в задачу.
- `github.ref_name` может иметь префикс `v` или другой формат, несовместимый с Velopack/package version. Workflow должен нормализовать версию или явно документировать требуемый tag format.
- GitHub rate limits возможны для unauthenticated public release checks; для public repo это приемлемо на MVP, но для private repo потребуется token/source configuration.
- Single-file publish может быть несовместим с выбранным Velopack packaging режимом или усложнить delta updates. В EXEC нужно проверить документацию/CLI фактически и при необходимости отключить single-file только для Velopack publish path, не меняя MSI path без причины.
- Несколько запущенных экземпляров приложения могут помешать применению обновления. MVP не добавляет single-instance lock.
- UI prompt через `DialogHost` должен вызываться только после готовности главного окна.
- Если пользователь открыл настройки до завершения фоновой проверки, ручная проверка должна либо дождаться текущей операции, либо быть временно отключена.
- Секция обновлений не должна перегружать страницу настроек длинными техническими ошибками; подробности можно оставить в логах, а UI показать короткий статус.

## 13. План выполнения
1. Подтвердить точные Velopack CLI аргументы на локальной версии `vpk` и зафиксировать stable версию пакета/tool.
2. Добавить Velopack dependency только в desktop проект через centralized package management.
3. Добавить ранний `VelopackApp.Build().Run()` в `Program.Main`.
4. Добавить platform-neutral update service contract и state model для `SettingsViewModel`.
5. Добавить desktop Velopack client wrapper.
6. Подключить update service и команды к `SettingsViewModel`.
7. Добавить секцию "Обновления" в `SettingsControl.axaml`.
8. Подключить фоновую проверку после открытия главного окна без блокировки UI.
9. Добавить/обновить локализованные строки для update section, prompt и statuses.
10. Обновить `.github/workflows/msi_packaging.yml` Velopack pack/upload steps, сохранив MSI/ZIP steps.
11. Добавить targeted unit/headless tests для update state и настроек.
12. Выполнить build/test/publish/vpk smoke checks.
13. Выполнить post-EXEC review и поправить найденные критичные проблемы.

## 14. Открытые вопросы
Нет блокирующих вопросов. Принятые допущения:
- Первый rollout охватывает `win`, `linux` и `osx` каналы, по одному Velopack feed на платформу.
- GitHub Releases публичного репозитория достаточно без PAT для runtime update checks.
- MSI/portable ZIP остаются параллельными артефактами минимум на переходный период.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - Длительные сетевые операции не блокируют UI thread.
  - Platform-specific Velopack код изолируется от ViewModel/бизнес-логики.
  - Существующие automation selectors не меняются.
  - Перед завершением EXEC должны быть запущены `dotnet build` и `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-04-23-velopack-github-updates.md` | Рабочая спецификация | QUEST gate перед реализацией |
| `src/Directory.Packages.props` | Добавить `Velopack`/`vpk` версию | Централизованное управление пакетами |
| `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj` | Добавить desktop-only Velopack dependency | Runtime hooks и update API |
| `src/Unlimotion.Desktop/Program.cs` | Ранний Velopack startup hook | Требование Velopack lifecycle |
| `src/Unlimotion.ViewModel/*` | Update service contract, status model, свойства и команды `SettingsViewModel` | Ручная проверка и применение обновлений из настроек без Velopack dependency |
| `src/Unlimotion.Desktop/Services/*` | Velopack implementation/client wrapper | Изоляция Velopack API и тестируемость |
| `src/Unlimotion/App.axaml.cs` | Подключение update service, команд и фоновой проверки | Запуск после готовности desktop UI |
| `src/Unlimotion/Views/SettingsControl.axaml` | Секция "Обновления" | Ручная проверка, скачивание и применение обновлений |
| `src/Unlimotion.ViewModel/Resources/Strings*.resx` | Строки update section/prompt/statuses | Локализация пользовательских сообщений |
| `src/Unlimotion.Test/*` | Coordinator tests | Проверка поведения без сети/GitHub |
| `.github/workflows/msi_packaging.yml` | Velopack packaging/upload steps для Windows | Публикация Windows update assets в GitHub Release |
| `.github/workflows/deb_packaging.yml` | Velopack packaging/upload steps для Linux + сохранение `.deb` | Публикация Linux update assets в GitHub Release |
| `.github/workflows/osx-packaging.yml` | Velopack packaging/upload steps для macOS + сохранение `.pkg` | Публикация macOS update assets в GitHub Release |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Startup lifecycle | Только app/Avalonia init | Ранний Velopack hook, затем обычный startup |
| Release assets | MSI + portable ZIP + `.deb` + `.pkg` | Те же артефакты + Velopack packages/feed для `win`, `linux`, `osx` |
| Runtime updates | Нет проверки | Background check через GitHub Releases |
| Settings updates | Нет раздела обновлений | Ручная проверка, скачивание и применение в настройках |
| User restart | Не применимо | Рестарт только после согласия пользователя |
| Non-Windows targets | Без Velopack | Без Velopack |

## 18. Альтернативы и компромиссы
- Вариант: заменить MSI на Velopack сразу.
  - Плюсы: меньше release artifacts.
  - Минусы: высокий rollout risk, нет fallback для текущих пользователей.
  - Почему не выбран: переходный период безопаснее.
- Вариант: только workflow packaging без runtime update check.
  - Плюсы: быстро и низкий риск.
  - Минусы: не решает автообновление.
  - Почему не выбран: пользователь запросил автообновление.
- Вариант: Advanced Installer Updater.
  - Плюсы: лучше ложится на текущий MSI.
  - Минусы: пользователь выбрал Velopack; хуже перспектива cross-platform.
  - Почему не выбран: Velopack выбран как целевое направление.
- Вариант: silent restart/update без prompt.
  - Плюсы: максимально автоматический сценарий.
  - Минусы: можно потерять пользовательский контекст.
  - Почему не выбран: безопаснее не перезапускать desktop-приложение без согласия.
- Вариант: только prompt при фоновой проверке без раздела настроек.
  - Плюсы: меньше UI и меньше команд.
  - Минусы: пользователь не может вручную проверить и применить обновление позже.
  - Почему не выбран: пользователь явно запросил раздел настроек для ручной проверки и применения.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели, ручной settings flow и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, алгоритм, UI state, состояние и rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Rollback, совместимость MSI/Velopack и edge cases отражены. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, тесты и команды проверки указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План этапов есть, блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | Требования `dotnet-desktop-client` явно учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Цель и Non-Goals определяют Windows-first Velopack MVP с ручным settings flow. |
| 2. Понимание текущего состояния | 5 | Зафиксированы Desktop entrypoint, shared app, settings structure и release workflow. |
| 3. Конкретность целевого дизайна | 5 | Есть lifecycle hook, update flow, settings UI state, workflow и точки интеграции. |
| 4. Безопасность (миграция, откат) | 5 | MSI fallback, rollback и несовместимость MSI -> Velopack отражены. |
| 5. Тестируемость | 5 | Указаны unit tests, build/test/publish/pack checks. |
| 6. Готовность к автономной реализации | 5 | План выполним без дополнительных решений пользователя. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: после уточнения пользователя добавлены раздел настроек, manual check/apply flow, runtime update states и no-op/unsupported поведение для не-Velopack установок.
- Что осталось на решение пользователя: требуется только подтверждение спеки фразой ниже.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор контекста | 0.9 | Нет | Создать SPEC | Нет | Нет | Найдены Avalonia Desktop entrypoint, centralized packages и текущий MSI/ZIP release workflow. | `src/Unlimotion.Desktop/Program.cs`, `.github/workflows/msi_packaging.yml`, `src/Directory.Packages.props` |
| SPEC | Проектирование | 0.85 | Нужна фактическая проверка CLI на EXEC | Запросить подтверждение | Да | Да, ожидается фраза `Спеку подтверждаю` | Velopack требует ранний startup hook и публикацию update assets; MSI канал сохранен как fallback. | `specs/2026-04-23-velopack-github-updates.md` |
| SPEC | Уточнение требований | 0.9 | Нет | Запросить подтверждение обновленной спеки | Да | Да, пользователь запросил раздел настроек для ручной проверки и применения | Расширил MVP: settings section, manual commands, update UI states и unsupported handling. | `specs/2026-04-23-velopack-github-updates.md`, `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion.ViewModel/SettingsViewModel.cs` |
| EXEC | Старт реализации | 0.9 | Нужна фактическая проверка Velopack API/CLI | Проверить API и внести изменения | Нет | Да, пользователь подтвердил спеку фразой `Спеку подтверждаю` | Переход в EXEC разрешен по QUEST gate. | `specs/2026-04-23-velopack-github-updates.md` |
| EXEC | Реализация core/update UI | 0.85 | Нужны результаты build/test | Запустить проверки | Нет | Нет | Добавлены update contract, состояния `SettingsViewModel`, секция настроек, локализация, Velopack desktop wrapper, startup hook и workflow pack/upload. | `src/Unlimotion.ViewModel/*`, `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion.Desktop/*`, `.github/workflows/msi_packaging.yml`, `src/Unlimotion.Test/SettingsViewModelTests.cs` |
| EXEC | Верификация | 0.9 | Нет | Провести post-EXEC review | Нет | Нет | `dotnet build src\Unlimotion.sln --no-restore -m:1`, desktop build, targeted `SettingsViewModelTests`, Release publish и `vpk pack` прошли; полный `dotnet test --no-build` блокируется существующими `BackupViaGitServiceTests` из-за Windows path-too-long, два соседних сбоя проходят изолированно. | `src/Unlimotion.sln`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `artifacts/velopack-smoke/*` |
| EXEC | Post-EXEC review/fix | 0.92 | Нет | Финализировать ответ | Нет | Нет | После ревью добавлен fallback версии приложения для не-Velopack запусков и исправлена PowerShell-валидация workflow: отсекается `0.0.0*`, используется корректный `-split`. | `src/Unlimotion.Desktop/Services/VelopackApplicationUpdateService.cs`, `.github/workflows/msi_packaging.yml` |
| EXEC | Продолжение верификации | 0.95 | Нет | Финализировать ответ | Нет | Да, пользователь сказал `продолжай` | Устранены blockers полного тестового прогона: Git backup fixtures перенесены в короткую temp-папку, settings-тест изолирован от глобальной локализации. Полный `dotnet test` теперь проходит: 198/198; `dotnet build src\Unlimotion.sln --no-restore -m:1` проходит. | `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `specs/2026-04-23-velopack-github-updates.md` |
| EXEC | Исправление review findings | 0.96 | Нет | Финализировать ответ | Нет | Да, пользователь сказал `испрравь` | Состояние pending restart теперь не теряется при ручной проверке обновлений, кнопка проверки блокируется до применения; workflow больше не принимает prerelease/build-теги, чтобы не передавать их в MSI `SetVersion`. Проверки: targeted settings tests 36/36, полный `dotnet test --no-build` 199/199, `dotnet build src\Unlimotion.sln --no-restore -m:1`, `git diff --check`. | `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion.Desktop/Services/VelopackApplicationUpdateService.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `.github/workflows/msi_packaging.yml` |
| EXEC | Rebase на основную ветку | 0.96 | Нет | Сделать коммит | Нет | Да, пользователь попросил новую ветку, rebase на `master` и commit; фактическая основная ветка репозитория - `main` | Создана `feature/velopack-github-updates` от `origin/main`, stash изменений применен с разрешением конфликтов. Для `main` добавлены Velopack references в Debian/macOS desktop-проекты, которые компилируют общий desktop entrypoint. Проверки после переноса: `dotnet restore src\Unlimotion.sln`, `dotnet build src\Unlimotion.sln --no-restore -m:1`, `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build` 201/201. | `src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj`, `src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.Test/*`, `specs/2026-04-23-velopack-github-updates.md` |
| EXEC | Multi-platform release feeds | 0.97 | Нет | Закоммитить и допушить PR | Нет | Да, пользователь попросил публиковать Velopack сразу для всех поддерживаемых платформ | Windows feed зафиксирован как `win`, в Linux/macOS workflows добавлены platform-specific Velopack pack/upload шаги и нормализация release version. Отдельные feed URLs не понадобились: используются каналы `win`, `linux`, `osx` в одном GitHub Release. Дополнительно исправлен `generate-osx-publish.sh`: restore теперь идет с `--runtime osx-x64`, иначе local macOS publish падал на `NETSDK1047`. Проверки: локальный `vpk pack --help`/`vpk upload github --help`, `dotnet publish` для `linux-x64`, `dotnet restore --runtime osx-x64` + `dotnet publish` для `osx-x64`, `git diff --check`. | `.github/workflows/msi_packaging.yml`, `.github/workflows/deb_packaging.yml`, `.github/workflows/osx-packaging.yml`, `src/Unlimotion.Desktop/ci/osx/generate-osx-publish.sh`, `specs/2026-04-23-velopack-github-updates.md` |
