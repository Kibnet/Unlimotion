# Periodic update checks

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до подтверждения спеки разрешено менять только этот файл; UI-facing изменение требует UI-тестов; не блокировать UI-поток сетевой проверкой обновлений
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, центральный `C:\Users\Kibnet\.codex\agents\AGENTS.md`

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
Добавить автоматическую периодическую проверку обновлений у уже запущенного приложения и дать пользователю управление этой проверкой в блоке настроек обновлений.

Outcome contract:
- Success means: по умолчанию приложение проверяет обновления автоматически каждый час; пользователь может выключить автопроверку и выбрать период в днях, часах или минутах; ручная проверка, скачивание и установка обновлений продолжают работать как раньше.
- Итоговый артефакт / output: изменения в ViewModel, Avalonia UI, локализации, app startup/timer logic и автоматические unit/headless UI тесты.
- Stop rules: остановиться после реализации, targeted UI/unit проверок, `dotnet build` и полного тестового прогона либо явно зафиксированной причины, почему конкретная проверка недоступна.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion/App.axaml.cs` создает `SettingsViewModel`, настраивает update commands и после инициализации вызывает `_ = CheckForUpdatesOnStartupAsync(vm.Settings)`.
- `CheckForUpdatesOnStartupAsync` сейчас делает однократную silent-проверку при старте, скачивает найденное обновление и показывает existing notification через `_notificationManager?.Ask(...)`.
- `src/Unlimotion.ViewModel/SettingsViewModel.cs` хранит состояние update flow: `UpdateState`, `CurrentApplicationVersion`, `AvailableUpdateVersion`, `UpdateStatusText`, `CanCheckForUpdates`, `CanDownloadUpdate`, `CanApplyUpdate`.
- Update provider contract находится в `src/Unlimotion.ViewModel/IApplicationUpdateService.cs`; менять его для этой задачи не нужно.
- `src/Unlimotion/Views/SettingsControl.axaml` уже содержит секцию `UpdatesTitle` с current/available version, status text и кнопками `CheckForUpdatesButton`, `DownloadUpdateButton`, `ApplyUpdateButton`.
- Локализация update UI живет в `src/Unlimotion.ViewModel/Resources/Strings.resx` и `Strings.ru.resx`.
- Тесты вокруг update settings уже есть в `src/Unlimotion.Test/SettingsViewModelTests.cs` и headless UI тест `SettingsControl_UpdateSection_ShowsVersionAndDownloadsAvailableUpdate` в `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs`.

Скрытые зависимости / инварианты:
- Settings persistence идет через `WritableJsonConfiguration` и `IConfigurationSection.Set(...)`.
- UI state обновляется из `SettingsViewModel`; XAML должен использовать стабильные `AutomationProperties.AutomationId`.
- Сетевая проверка обновлений должна оставаться async и не выполнять длительную синхронную работу на UI thread.
- Quartz scheduler в `App.axaml.cs` сейчас относится к Git backup; update timer лучше держать отдельно, чтобы не смешивать настройки backup и updates.

## 3. Проблема
После старта приложение больше не проверяет обновления, поэтому пользователь может долго работать в уже открытом приложении и не узнать о вышедшей версии без ручного действия или перезапуска.

## 4. Цели дизайна
- Разделение ответственности: `SettingsViewModel` хранит persisted настройки автопроверки и вычисляет интервал; `App` управляет runtime timer и вызывает существующий update flow; XAML только отображает и биндует controls.
- Повторное использование: использовать существующие `CheckForUpdatesAsync`, `DownloadUpdateAsync`, `ApplyUpdateAsync` и notification text.
- Тестируемость: проверить persistence/defaults на уровне ViewModel, UI binding в Avalonia.Headless и automatic update runner без ожидания реального часа.
- Консистентность: настройки добавить в существующий блок Updates, с локализацией EN/RU и automation-id.
- Обратная совместимость: при отсутствии новых ключей конфигурации использовать default `enabled=true`, `1 hour`; существующие configs остаются валидными.

## 5. Non-Goals (чего НЕ делаем)
- Не менять реализацию `VelopackApplicationUpdateService` и `AndroidApplicationUpdateService`.
- Не менять публичный contract `IApplicationUpdateService`.
- Не добавлять новый системный сервис, background worker process или Quartz job для updates.
- Не менять Git backup intervals и advanced backup settings.
- Не менять release channel, endpoint GitHub Releases или формат update metadata.
- Не делать UX для "проверять только при Wi-Fi/заряде/idle".

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.ViewModel/ApplicationUpdateSettings.cs` -> constants/defaults для секции `Updates`, unit enum и interval normalization.
- `src/Unlimotion.ViewModel/SettingsViewModel.cs` -> persisted properties: auto-check enabled, numeric interval value, selected unit/index, computed `TimeSpan`.
- `src/Unlimotion.ViewModel/SettingsUiState.cs` -> при необходимости enum для UI state, если не вынесен в новый файл.
- `src/Unlimotion/App.axaml.cs` -> `DispatcherTimer` или эквивалентный UI-thread timer, подписки на настройки, запуск/остановка/reschedule, защита от overlapping checks, reusable automatic check method.
- `src/Unlimotion/Views/SettingsControl.axaml` -> checkbox, numeric input, unit combo внутри Updates section.
- `src/Unlimotion.ViewModel/Resources/Strings.resx`, `Strings.ru.resx` -> новые labels/options.
- `src/Unlimotion.Test/SettingsViewModelTests.cs` -> defaults, persistence, interval conversion/normalization.
- `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` -> headless UI coverage для checkbox/input/unit selector.

### 6.2 Детальный дизайн
- Добавить persisted section `Updates`:
  - `AutoCheckEnabled`: bool, default `true`.
  - `CheckIntervalValue`: int, default `1`.
  - `CheckIntervalUnit`: string или enum-name, default `Hours`.
- Поддерживаемые units: `Days`, `Hours`, `Minutes`.
- `SettingsViewModel` читает absent/invalid config как default `true`, `1`, `Hours`; при setter пишет normalized value обратно в `IConfiguration`.
- Numeric value нормализуется минимум до `1`; верхняя граница в UI может быть консервативной (`999`), чтобы избежать бессмысленных значений и overflow.
- Computed interval:
  - Days -> `TimeSpan.FromDays(value)`.
  - Hours -> `TimeSpan.FromHours(value)`.
  - Minutes -> `TimeSpan.FromMinutes(value)`.
- UI в Updates section:
  - `CheckBox` `AutomationId="UpdateAutoCheckEnabledCheckBox"` bound to `UpdateAutoCheckEnabled`.
  - `NumericUpDown` `AutomationId="UpdateCheckIntervalValueInput"` bound to `UpdateCheckIntervalValue`.
  - `ComboBox` `AutomationId="UpdateCheckIntervalUnitComboBox"` bound to `UpdateCheckIntervalUnitIndex`, с localized items `Days`, `Hours`, `Minutes`.
  - interval controls disabled or hidden when auto-check disabled; предпочтительно disabled, чтобы настройка была видна и сохранялась.
- `App` после создания/настройки `SettingsViewModel` вызывает setup automatic update timer.
- Timer ownership/lifecycle:
  - timer хранится как instance field `App`, а не static global state.
  - `App.ConfigureUpdateService(...)` остается допустимым до и после создания `MainWindowViewModel`: он сохраняет `_applicationUpdateService`, конфигурирует existing `SettingsViewModel`, если он уже создан, и затем просит текущий `App` пересчитать timer, если `Application.Current` уже доступен.
  - если `SettingsViewModel` создается после `ConfigureUpdateService(...)`, `GetMainWindowViewModel()` вызывает `settingsViewModel.ConfigureUpdateService(_applicationUpdateService)` и setup/reschedule timer после `SetupSettingsCommands(settingsViewModel)`.
  - при повторном setup старые subscriptions/timer не дублируются; reschedule использует один existing timer.
- Timer behavior:
  - если auto-check disabled, timer stopped.
  - если update service unsupported, timer stopped or no-op без сетевых вызовов.
  - если interval меняется, timer interval обновляется без перезапуска приложения.
  - первый periodic tick происходит не сразу, а через выбранный интервал после setup/reschedule; стартовая проверка остается отдельным automatic trigger.
  - startup automatic check и periodic tick вызывают один общий automatic runner и используют один `_isAutomaticUpdateCheckRunning` guard.
  - automatic flow повторно использует существующие методы: silent check -> download if update available -> ask install if ready.
  - если state уже busy/applying/ready-to-apply, timer не начинает новую сетевую проверку и не спамит повторными prompts.
  - ручные кнопки не проходят через automatic runner; они сохраняют current commands и опираются на existing `IsUpdateBusy`/`Can*` state.
- output/evidence rules:
  - evidence для defaults/persistence: unit tests по config values.
  - evidence для UI: Avalonia.Headless test по automation-id и фактическому изменению VM.
  - evidence для runtime runner: unit/integration test без ожидания реального timer interval через internal method или другой test seam, не через sleep на час.
- Обработка ошибок:
  - использовать existing `CheckForUpdatesAsync(silent: true)` для periodic checks, чтобы ошибка не показывала noisy exception text.
  - при ошибке state переходит в `Error`, следующий tick может повторить проверку.
- Производительность:
  - timer tick короткий; сетевой запрос async.
  - no polling быстрее выбранного пользователем интервала.

## 7. Бизнес-правила / Алгоритмы (если есть)
| Условие | Поведение |
| --- | --- |
| Новый config без `Updates` | Автопроверка включена, интервал `1 hour` |
| `AutoCheckEnabled=false` | Timer остановлен, startup/periodic automatic checks no-op, ручные кнопки не меняются |
| `CheckIntervalValue<=0` или отсутствует | В памяти используется `1`; при первом setter/нормализации через dedicated method значение сохраняется как `1` |
| `CheckIntervalUnit` отсутствует/неизвестен | В памяти используется `Hours`; при первом setter/нормализации через dedicated method значение сохраняется как `Hours` |
| Update service unsupported | UI показывает existing unsupported state, timer не делает сетевых вызовов |
| Tick во время проверки/скачивания/установки | Новый tick игнорируется |
| Найдено обновление | Existing automatic flow скачивает update и показывает existing apply prompt |

## 8. Точки интеграции и триггеры
- `App.GetMainWindowViewModel()` / `SetupSettingsCommands(settings)` -> добавить настройку update timer после update commands и подписок.
- `InitializeStartupViewModelAsync` -> сохранить стартовую проверку; periodic timer работает дополнительно после инициализации.
- `App.ConfigureUpdateService(...)` -> при смене update service обновлять `SettingsViewModel`, если он уже создан, и пересчитать availability timer через текущий instance `App`, если он уже существует.
- `SettingsViewModel` property setters -> сохранять настройки и поднимать notifications для dependent interval/index properties.
- `SettingsViewModel.NormalizeUpdateCheckSettings()` или эквивалентный internal/helper method -> явно записывать normalized defaults для invalid config, чтобы constructor не вносил неожиданный config churn.

## 9. Изменения модели данных / состояния
- Новая persisted section `Updates`:
  - `AutoCheckEnabled` bool.
  - `CheckIntervalValue` int.
  - `CheckIntervalUnit` string.
- Эти поля не требуют миграции файлов задач и не меняют доменную модель.
- Existing configs без секции читаются с default значениями без немедленной записи; ключи появляются при изменении пользователем или при explicit normalization invalid values.

## 10. Миграция / Rollout / Rollback
- Первый запуск после обновления: missing `Updates` treated as enabled hourly.
- Backward compatibility: старые версии приложения игнорируют неизвестную секцию `Updates`.
- Rollback: удалить новые ключи или выставить `Updates:AutoCheckEnabled=false`; update service contract остается прежним.
- Не требуется миграция пользовательских задач, Git backup или server data.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - По умолчанию `SettingsViewModel` показывает включенную автопроверку с интервалом `1 hour`.
  - Пользователь может выключить автопроверку в Updates section; значение сохраняется в config.
  - Пользователь может выбрать числовое значение и units `days/hours/minutes`; интервал корректно конвертируется в `TimeSpan` и сохраняется.
  - При включенной автопроверке runtime logic вызывает update check по timer tick без overlap.
  - При unsupported update service timer не выполняет сетевую проверку.
  - Manual update buttons keep existing behavior.
  - Settings UI не ломает narrow viewport test.
- Какие тесты добавить/изменить:
  - `SettingsViewModelTests`: defaults/persistence/conversion для automatic update settings.
  - `SettingsViewModelTests` или отдельный тест `App`: direct automatic runner test с fake update service, подтверждающий check/download path и no-op when disabled/unsupported.
  - `SettingsControlResponsiveUiTests`: headless test для checkbox, numeric input, unit combo в Updates section.
  - Обновить existing update section UI test при необходимости, не удаляя coverage кнопок.
- Characterization tests / contract checks для текущего поведения:
  - Существующий `SettingsControl_UpdateSection_ShowsVersionAndDownloadsAvailableUpdate` должен продолжить проходить.
  - Существующие `CheckForUpdatesAsync_*`, `DownloadUpdateAsync_*`, `ApplyUpdateAsync_*` должны продолжить проходить.
- Базовые замеры до/после для performance tradeoff: не применимо; изменение timer-based, без render/perf hot path.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/SettingsViewModelTests/*"`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/SettingsControlResponsiveUiTests/*"`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`
- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted test падает, исправить и повторить targeted.
  - Если full suite падает на явно unrelated known issue, зафиксировать evidence и affected tests; иначе исправить до завершения.
  - Если runner требует другой синтаксис TUnit/Microsoft.Testing.Platform, использовать `--list-tests` и корректный `--treenode-filter`.

## 12. Риски и edge cases
- Риск: startup check и timer tick могут пересечься. Смягчение: единый automatic runner и `_isAutomaticUpdateCheckRunning` guard для обоих automatic triggers; первый timer tick только после полного interval.
- Риск: property changes из background thread могут ломать Avalonia bindings. Смягчение: использовать `DispatcherTimer`/UI thread или marshal через `Dispatcher.UIThread`.
- Риск: periodic prompt может повторяться. Смягчение: не запускать automatic flow, когда state уже `ReadyToApply`/busy.
- Риск: invalid config values. Смягчение: normalize value/unit in ViewModel.
- Риск: новая UI строка ломает responsive layout. Смягчение: headless narrow viewport test remains in targeted suite.
- Риск: `dotnet test src/Unlimotion.sln` может быть долгим или зависеть от platform workloads. Смягчение: сначала targeted, потом полный прогон; если full недоступен, явно отчитаться с причиной.

## 13. План выполнения
1. Добавить update settings model/enum/defaults в ViewModel layer.
2. Расширить `SettingsViewModel` persisted properties и computed interval.
3. Добавить localized labels/options EN/RU.
4. Обновить `SettingsControl.axaml` controls в Updates section с automation-id.
5. Добавить runtime automatic update timer в `App.axaml.cs`, переиспользуя existing update flow, сохраняя startup check и явно поддерживая оба порядка `ConfigureUpdateService` относительно создания VM.
6. Добавить/обновить unit и Avalonia.Headless tests.
7. Запустить targeted tests, build, full tests.
8. Выполнить post-EXEC review и исправить критичные/high-confidence находки.

## 14. Открытые вопросы
Нет блокирующих вопросов.

Принятое допущение: periodic automatic flow должен вести себя как стартовая автоматическая проверка: тихо проверить, скачать найденное обновление и показать existing prompt на установку. Это согласовано с текущим AS-IS поведением при старте и не требует нового UX.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - UI thread не блокируется длительной синхронной работой; update checks остаются async.
  - UI/integration tests будут обновлены для нового UI flow.
  - Automation-id будут стабильными и не текстовыми.
  - Перед завершением planned `dotnet build` и `dotnet test`.
- Профиль: `ui-automation-testing`
- Выполненные требования профиля:
  - Используется существующий Avalonia.Headless suite.
  - Добавляется UI coverage для новых controls.
  - Селекторы через `AutomationProperties.AutomationId`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-05-12-periodic-update-checks.md` | Рабочая спецификация | QUEST gate |
| `src/Unlimotion.ViewModel/ApplicationUpdateSettings.cs` | Defaults, enum/unit parsing, interval normalization | Централизовать правила update settings |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | Persisted properties и computed interval | Состояние настроек для UI и timer |
| `src/Unlimotion/Views/SettingsControl.axaml` | Checkbox, numeric input, units combo | Пользовательское управление автопроверкой |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | EN строки | Локализация UI |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | RU строки | Локализация UI |
| `src/Unlimotion/App.axaml.cs` | Timer setup/reschedule/automatic runner | Периодическая проверка в запущенном приложении |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | Unit coverage | Defaults/persistence/conversion/runner behavior |
| `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` | Headless UI coverage | Обязательное UI-покрытие |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Automatic updates | Однократная проверка после старта | Стартовая проверка + periodic check while running |
| Defaults | Нет пользовательской настройки периодичности | Auto-check enabled, interval 1 hour |
| UI | Только current/available version, status, buttons | Добавлены checkbox и interval selector |
| Persistence | Нет секции update periodicity | `Updates` section with enabled/value/unit |
| Tests | Update buttons covered | Buttons + auto-check settings + runner behavior covered |

## 18. Альтернативы и компромиссы
- Вариант: Quartz job рядом с Git backup.
- Плюсы: единая scheduler dependency.
- Минусы: смешивает независимые подсистемы, текущий Quartz scheduler лениво создается вокруг backup service and file mode.
- Почему выбранное решение лучше в контексте этой задачи: lightweight app-level timer меньше связан с Git backup, проще тестируется и не меняет lifecycle существующих backup jobs.

- Вариант: хранить interval только в секундах.
- Плюсы: проще runtime conversion.
- Минусы: UI с numeric value + unit требует обратной нормализации и может менять выбранную пользователем единицу.
- Почему выбранное решение лучше в контексте этой задачи: хранение value+unit сохраняет ровно то, что выбрал пользователь.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals описаны |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, правила, ошибки, perf раскрыты |
| C. Безопасность изменений | 11-13 | PASS | Persisted keys, compatibility, rollback и edge cases описаны |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, тесты и команды проверки указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | План есть, блокирующих вопросов нет, масштаб ограничен |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client` и `ui-automation-testing` требования учтены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Требование сведено к periodic update checks и UI settings |
| 2. Понимание текущего состояния | 5 | Указаны текущие классы, XAML, ресурсы и тесты |
| 3. Конкретность целевого дизайна | 5 | Описаны config keys, bindings, timer behavior и no-overlap |
| 4. Безопасность (миграция, откат) | 5 | Missing config defaults, backward compatibility и rollback определены |
| 5. Тестируемость | 5 | Есть unit, runner и Avalonia.Headless acceptance checks |
| 6. Готовность к автономной реализации | 5 | Нет открытых вопросов, план и ограничения достаточны |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлен explicit no-overlap guard, правило unsupported service no-op, accepted assumption по automatic download/prompt flow, evidence rule без ожидания реального часа, уточнен timer lifecycle для раннего/позднего `ConfigureUpdateService`, разведены startup/periodic/manual triggers и нормализация config без constructor churn.
- Что осталось на решение пользователя: требуется только утверждение спеки фразой из секции Approval.

### Post-EXEC Review
- Статус: PASS с ограничениями валидации
- Что исправлено до завершения: тестом найден и исправлен дефект `NormalizeUpdateCheckSettings`, который не переписывал invalid config value из-за setter short-circuit; UI-test height увеличен для update-specific сценариев, потому что новые controls сдвинули кнопки ниже видимой области.
- Что проверено дополнительно для refactor / comments: публичный update service contract не изменен; automatic runner отделен от ручных команд; startup/periodic checks используют общий guard; новых code comments не добавлено.
- Остаточные риски / follow-ups: полный `dotnet build src/Unlimotion.sln` блокируется на существующем Android project build; полный `Unlimotion.Test` run зависает на существующем worktree UI-сценарии `SettingsControl_LocalStorageConnect_UsesEditedTaskStoragePath`. Релевантные targeted checks пройдены.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Собрать instruction stack и маршрут | 0.95 | Нет | Создать рабочую спецификацию | Нет | Нет | Задача классифицирована как delivery-task для .NET desktop UI с обязательным UI testing overlay | `AGENTS.md`, `AGENTS.override.md`, central instructions |
| SPEC | Собрать AS-IS контекст update flow/settings/tests | 0.9 | Нет | Сформировать TO-BE и quality gate | Нет | Нет | Найдены существующие update methods, Settings UI и headless tests, поэтому дизайн ограничен их расширением | `App.axaml.cs`, `SettingsViewModel.cs`, `SettingsControl.axaml`, test files |
| SPEC | Подготовить spec, linter/rubric и post-SPEC review | 0.9 | Нет | Запросить утверждение спеки | Да | Да, ожидается фраза `Спеку подтверждаю` | Центральный QUEST gate запрещает менять код до подтверждения | `specs/2026-05-12-periodic-update-checks.md` |
| SPEC | Исправить замечания ревью спеки | 0.92 | Нет | Запросить утверждение обновленной спеки | Да | Да, пользователь попросил исправить ревью-находки | Уточнены timer lifecycle, no-overlap semantics и правила persistence defaults без изменения кода | `specs/2026-05-12-periodic-update-checks.md` |
| EXEC | Добавить модель persisted update settings | 0.9 | Нет | Обновить UI и локализацию | Нет | Нет | Вынесены defaults/unit conversion и добавлены свойства `SettingsViewModel` для checkbox/value/unit без constructor config churn | `src/Unlimotion.ViewModel/ApplicationUpdateSettings.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs` |
| EXEC | Обновить Updates UI и ресурсы | 0.9 | Нет | Добавить runtime timer и automatic runner | Нет | Нет | В существующий блок обновлений добавлены checkbox, числовой ввод и выбор единиц с EN/RU локализацией и automation-id | `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion.ViewModel/Resources/Strings.resx`, `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` |
| EXEC | Добавить runtime automatic update runner | 0.86 | Нет | Добавить unit/headless UI тесты | Нет | Нет | `App` получил один timer, reschedule по настройкам/update service и общий no-overlap runner для startup/periodic checks | `src/Unlimotion/App.axaml.cs`, `specs/2026-05-12-periodic-update-checks.md` |
| EXEC | Добавить тестовое покрытие | 0.86 | Нет | Запустить targeted tests | Нет | Нет | Добавлены unit tests для defaults/persistence/normalization/automatic runner и headless UI test для новых controls | `src/Unlimotion.Test/SettingsViewModelTests.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` |
| EXEC | Запустить targeted проверки и исправить найденные дефекты | 0.88 | Нет | Запустить build/full checks | Нет | Нет | `SettingsViewModelTests` выявил дефект normalization helper, затем прошел 48/48; релевантные headless update/new/narrow tests прошли по одному | `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` |
| EXEC | Запустить build/full checks и post-EXEC review | 0.82 | Нет | Сформировать финальный отчет | Нет | Нет | `Unlimotion.Test.csproj` build прошел; solution build и full test project run заблокированы существующими Android/worktree зависаниями, процессы остановлены | `specs/2026-05-12-periodic-update-checks.md` |
