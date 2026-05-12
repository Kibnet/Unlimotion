# Android startup crash after self-update

## 0. Метаданные
- Тип (профиль): delivery-task; context `testing-dotnet`; stack profile `dotnet-desktop-client` как ближайший Avalonia client profile; overlay `ui-automation-testing`.
- Владелец: Codex / Kibnet.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущий worktree.
- Ограничения: до подтверждения спеки менять только этот файл; публичный API не менять; Android storage path и user data сохранить.
- Связанные ссылки: `adb logcat -b crash`, установленный пакет `com.Kibnet.Unlimotion` versionName `1.24.0`, versionCode `169`.

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
Исправить падение Android-приложения сразу после автообновления, когда Avalonia Android application lifetime создаёт `Unlimotion.App` раньше, чем `MainActivity.OnCreate` успевает выполнить `App.Init(...)`.

Outcome contract:
- Success means: установленная Android-сборка запускается на подключённом устройстве после обновления/повторного старта без `NullReferenceException` в `SettingsViewModel`.
- Итоговый артефакт / output: минимальный кодовый фикс порядка Android bootstrap, regression coverage в существующем UI/test контуре, подтверждение через `adb` smoke.
- Stop rules: остановиться после подтверждённого запуска на устройстве и прохождения релевантных targeted tests; если Android instrumentation недоступен, явно зафиксировать это и выполнить next-best headless UI + `adb` launch check.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion.Android/MainActivity.cs` вызывает `ConfigureAppServices()` в `MainActivity.OnCreate` до `base.OnCreate`.
- В этом методе создаются data directory, `Settings.json`, вызывается `App.Init(configPath)`, настраиваются git safe directory/cert bundle, update service и Android folder picker callbacks.
- В APK `1.24.0` crash stack показывает, что `Avalonia.Android.AvaloniaAndroidApplication<Unlimotion.App>.OnCreate` вызывает `App.OnFrameworkInitializationCompleted` раньше activity bootstrap.
- `App.GetMainWindowViewModel()` создаёт `SettingsViewModel(_configuration!, ...)`; если `App.Init` ещё не выполнен, `_configuration` и/или `LocalizationService.Current` не инициализированы.
- Подключённый телефон: `SM-F956B`; crash buffer содержит `System.NullReferenceException` в `Unlimotion.ViewModel.SettingsViewModel..ctor`, далее `App.GetMainWindowViewModel`.

## 3. Проблема
Одна корневая проблема: Android app-level bootstrap расположен в `Activity.OnCreate`, но фактический Avalonia application setup выполняется в Android `Application.OnCreate`, поэтому `SettingsViewModel` создаётся до инициализации статических сервисов `App`.

## 4. Цели дизайна
- Разделение ответственности: app-level сервисы инициализируются в Android `Application`, Activity-only сервисы остаются в `MainActivity`.
- Повторное использование: не дублировать расчёт data directory, git safe directory и cert bundle между `AndroidApp` и `MainActivity`.
- Тестируемость: зафиксировать startup smoke в существующем UI/test контуре и проверить реальный APK на устройстве.
- Консистентность: сохранить текущие пути `Settings.json`, `Tasks`, `.gitconfig`, `cacert.pem`.
- Обратная совместимость: существующий config и пользовательские данные после автообновления должны читаться из того же app-private external files directory.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем формат настроек, `ApplicationId`, подпись, versioning или механизм скачивания APK.
- Не переписываем update service и Git backup flow.
- Не добавляем новый Android instrumentation framework без отдельного решения.
- Не удаляем пользовательские данные и не требуем ручной очистки приложения.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Android/MainActivity.cs` -> оставить Activity callbacks: update service, folder picker, storage permission flow.
- Новый/выделенный Android bootstrap helper в `src/Unlimotion.Android/MainActivity.cs` или соседнем файле -> core bootstrap, доступный из Android `Application` и `Activity`.
- `AndroidApp : AvaloniaAndroidApplication<App>` -> до `base.OnCreate()` выполнить core bootstrap: data dir, config file, `App.Init(configPath)`, git safe directory/cert bundle.
- Тесты -> расширить существующий UI/startup coverage без Android-only зависимостей; device smoke подтвердит реальный Android lifecycle.

### 6.2 Детальный дизайн
- Поток данных:
  - `AndroidApp.OnCreate` получает application context, вычисляет тот же data directory через `GetExternalFilesDir(null)` fallback `FilesDir`.
  - Создаёт каталог, `Settings.json` с `{}` при отсутствии, выставляет `App.DefaultStoragePath` и `TaskStorageFactory.DefaultStoragePath` в `Path.Combine(dataDir, "Tasks")` до `App.Init(configPath)`.
  - Вызывает `App.Init(configPath)` до `base.OnCreate()`, чтобы `App.OnFrameworkInitializationCompleted` видел готовую конфигурацию.
  - `MainActivity.OnCreate` больше не повторяет core `App.Init`; он настраивает `AndroidApplicationUpdateService(this)`, `Dialogs.PlatformOpenFolderDialogAsync`, `TaskStorageFactory.PrepareFileStoragePathAsync`.
- Startup update flow:
  - Если `App.OnFrameworkInitializationCompleted` уже создал `SettingsViewModel` до появления Activity, `App.ConfigureUpdateService(...)` обязан не только прикрепить service к существующим settings, но и запустить/переиграть startup update check безопасно после attachment.
  - Startup update check не должен падать и не должен навсегда пропускаться из-за временно отсутствующего Activity-dependent service.
- Контракты / API: публичный API не меняется; возможен internal helper для Android-проекта.
- Evidence rules: crash считается исправленным только если `adb logcat -b crash` после запуска не содержит нового `FATAL EXCEPTION` по `com.Kibnet.Unlimotion`, а `pidof` показывает живой процесс или UI остаётся открытым.
- Границы сохранения поведения: storage path и cert path должны совпадать с текущими путями в app-private storage.
- Обработка ошибок: startup exceptions продолжают писаться через `WriteStartupError`; helper не должен молча пропускать `App.Init` ошибки.
- Производительность: одна инициализация app-level сервисов за запуск; helper должен быть idempotent на случай повторных вызовов.

## 7. Бизнес-правила / Алгоритмы (если есть)
- App-level configuration MUST be ready before Avalonia creates the single-view lifetime.
- Activity-dependent services MUST be configured after an Activity instance exists.
- Repeated bootstrap calls MUST preserve the first successful config path and avoid destructive reinitialization.
- Android default task storage MUST be assigned to both `App.DefaultStoragePath` and `TaskStorageFactory.DefaultStoragePath` before `App.Init(...)`.
- Startup update check MUST run only when an `IApplicationUpdateService` is available, or be replayed after `App.ConfigureUpdateService(...)` attaches one.

## 8. Точки интеграции и триггеры
- Android `Application.OnCreate`: core bootstrap before Avalonia `base.OnCreate`.
- `MainActivity.OnCreate`: Activity services before/around `base.OnCreate` without re-running core init.
- `App.ConfigureUpdateService`: updates existing `SettingsViewModel` if it already exists and triggers deferred startup update check when applicable.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Existing `Settings.json`, `Tasks`, `.gitconfig`, `cacert.pem` remain in the same data directory.
- Runtime-only state: idempotency flag/path for Android bootstrap if needed.

## 10. Миграция / Rollout / Rollback
- Первый запуск после фикса: existing data dir is reused; missing `Settings.json` is created as before.
- Обратная совместимость: version `1.24.0` data remains readable.
- Rollback: вернуть core bootstrap в Activity; риск rollback - восстановление startup crash.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `adb` воспроизводит исходный crash до фикса: `NullReferenceException` in `SettingsViewModel..ctor`.
  - После фикса Android APK installs/updates and launches on connected `SM-F956B` without `FATAL EXCEPTION`.
  - Default local task storage remains under the Android app data directory after first launch.
  - Startup update check is not lost when `SettingsViewModel` is created before `AndroidApplicationUpdateService`.
  - Existing Settings/update UI remains constructible in headless UI tests.
  - `dotnet build` succeeds for affected projects.
- Какие тесты добавить/изменить:
  - Add/update a targeted headless UI startup/package compatibility test in existing `src/Unlimotion.Test` or `tests/Unlimotion.UiTests.Headless` patterns.
  - No Android instrumentation suite exists in repo; cover Android-specific lifecycle with build + device `adb` launch smoke.
- Characterization tests / contract checks:
  - Existing `SingleViewStartupUiTests` and `PackageUpdateCompatibilityUiTests` remain passing.
- Базовые замеры до/после для performance tradeoff: не применимо, startup sequencing bug without performance target.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --list-tests`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/PackageUpdateCompatibilityUiTests/*"`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/SingleViewStartupUiTests/*"`
  - `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Release`
  - install produced APK via `adb install -r <apk>`
  - `adb logcat -c`; `adb shell monkey -p com.Kibnet.Unlimotion -c android.intent.category.LAUNCHER 1`; `adb logcat -b crash -d -v time`
  - If feasible within time, full `dotnet test src/Unlimotion.sln`.
- Stop rules для test/retrieval/tool/validation loops:
  - Continue diagnostics only if crash persists or a new startup exception appears.
  - Stop validation when targeted tests, Android build, and device launch smoke pass; report any full-suite limitations.

## 12. Риски и edge cases
- `AndroidApplicationUpdateService` still requires Activity; startup auto-check may run before service is attached. Mitigation: make startup update check service-aware and replay it from `App.ConfigureUpdateService` after attachment.
- Duplicate `App.Init` can reset runtime state. Mitigation: make Android bootstrap idempotent or remove duplicate call from Activity path.
- `Assets.Open("cacert.pem")` availability in Android `Application` context must be verified by build/device smoke.
- Existing data path must not accidentally move from external files dir to internal files dir.

## 13. План выполнения
1. Reproduce/record crash evidence from `adb` and identify lifecycle ordering.
2. Refactor Android bootstrap so core `App.Init` runs in Android `Application.OnCreate` before Avalonia setup.
3. Set both `App.DefaultStoragePath` and `TaskStorageFactory.DefaultStoragePath` before `App.Init`.
4. Keep Activity-only update/folder/permission services in `MainActivity`.
5. Defer/replay startup update check after `App.ConfigureUpdateService` attaches Android update service.
6. Add/update targeted UI regression coverage in existing test suite.
7. Run targeted UI tests and Android build.
8. Install/launch on connected phone and verify crash buffer.
9. Run full available test suite or explicitly report blocker/time/environment limitation.

## 14. Открытые вопросы
Нет блокирующих вопросов. Требуется только подтверждение спеки перед изменениями кода.

## 15. Соответствие профилю
- Профиль: `ui-automation-testing` + `testing-dotnet`.
- Выполненные требования профиля:
  - Баг воспроизведён фактическим device crash log.
  - План включает regression UI coverage и запуск релевантных UI tests.
  - План включает Android build and real-device UI launch smoke because repo has no Android instrumentation suite.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Android/MainActivity.cs` | Move/extract core Android bootstrap to run from `AndroidApp.OnCreate`; keep Activity services in `MainActivity`; set both default storage path properties | Ensure config and Android storage path exist before Avalonia app setup |
| `src/Unlimotion/App.axaml.cs` | Make startup update check service-aware/deferred after `ConfigureUpdateService` if needed | Prevent update check from being skipped because Activity service attaches later |
| `src/Unlimotion.Test/PackageUpdateCompatibilityUiTests.cs` или близкий UI test file | Add/update startup/package compatibility regression | Satisfy UI-facing regression coverage |
| `specs/2026-05-12-android-startup-after-update.md` | Working QUEST spec and journal | Required central workflow |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Android bootstrap | `App.Init` in `MainActivity.OnCreate` | Core `App.Init` before Avalonia setup in Android `Application.OnCreate` |
| Activity services | Mixed with app-level init | Activity-only services remain in `MainActivity` |
| Default task storage | Risk of falling back to desktop local app data if only `TaskStorageFactory.DefaultStoragePath` is set | `App.DefaultStoragePath` and `TaskStorageFactory.DefaultStoragePath` both point to Android app data |
| Startup update check | Can run before Android update service is attached | Deferred/replayed after service attachment |
| Startup after update | Crash before UI | UI launches with initialized configuration |
| Test evidence | Manual crash log only | Regression UI test + Android build + device launch smoke |

## 18. Альтернативы и компромиссы
- Вариант: make `App.GetMainWindowViewModel` lazily call `App.Init` if `_configuration` is null.
- Плюсы: small change in shared app layer.
- Минусы: Android data directory and Activity services become ambiguous; risk of config path drift and duplicate initialization.
- Почему выбранное решение лучше в контексте этой задачи: root cause is Android lifecycle ordering, so initializing app-level services at Android `Application` scope preserves platform ownership and existing storage behavior.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, state и rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Есть критерии, риски, план и rollback note. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, targeted tests, Android smoke и affected files указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, tradeoff и review заполнены. |
| F. Соответствие профилю | 20 | PASS | UI/test требования учтены с ограничением по Android instrumentation. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна crash-проблема, явные Non-Goals. |
| 2. Понимание текущего состояния | 5 | Есть device evidence и конкретный lifecycle path. |
| 3. Конкретность целевого дизайна | 5 | Указаны компоненты и sequencing. |
| 4. Безопасность (миграция, откат) | 5 | Data path, idempotency, rollback risk описаны. |
| 5. Тестируемость | 5 | Targeted UI, build и `adb` smoke commands заданы. |
| 6. Готовность к автономной реализации | 5 | Открытых блокеров нет; план пошаговый. |

Итоговый балл: 30 / 30  
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: после review уточнены `App.DefaultStoragePath`, deferred startup update check и TUnit-compatible test commands.
- Что осталось на решение пользователя: только формальное подтверждение перехода в EXEC.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Диагностика Android crash | 0.90 | Нужна проверка фикса на новом APK | Сформировать и согласовать spec | Да | Да, требуется подтверждение спеки | `adb logcat -b crash` показал NRE в `SettingsViewModel` при Android Application startup | Устройство `SM-F956B`, `src/Unlimotion.Android/MainActivity.cs`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs` |
| SPEC | Подготовка плана фикса | 0.88 | Нет блокирующих данных | Ожидать "Спеку подтверждаю" | Да | Да, запрос подтверждения ниже | Central QUEST требует spec-first перед кодом | `specs/2026-05-12-android-startup-after-update.md` |
| SPEC | Внесение review-правок в spec | 0.92 | Нет блокирующих данных | Ожидать "Спеку подтверждаю" | Да | Да, пользователь попросил внести изменения | Review выявил пропущенный `App.DefaultStoragePath`, race update service и неверный TUnit filter syntax | `specs/2026-05-12-android-startup-after-update.md` |
| EXEC | Подтверждение спеки | 0.92 | Нужна проверка после реализации | Перейти к Android bootstrap fix | Нет | Да, пользователь написал "Спеку подтверждаю" | QUEST gate открыт для изменений кода в границах spec | `specs/2026-05-12-android-startup-after-update.md` |
| EXEC | Android bootstrap и update replay | 0.82 | Нужны compile/test/device проверки | Добавить regression tests | Нет | Нет | Core bootstrap перенесён в Android `Application.OnCreate`; startup update check стал service-aware | `src/Unlimotion.Android/MainActivity.cs`, `src/Unlimotion/App.axaml.cs` |
| EXEC | Regression UI test | 0.84 | Нужен targeted test run | Запустить TUnit targeted tests | Нет | Нет | Добавлен headless startup сценарий, где update service подключается после startup | `src/Unlimotion.Test/SingleViewStartupUiTests.cs` |
| EXEC | Валидация targeted tests и APK package | 0.90 | Нужен повторный `adb` smoke при подключённом устройстве | Дождаться устройства или передать результат | Нет | Нет | Targeted UI tests прошли; Android arm64 Debug package собран, device сейчас не виден в `adb devices` | `src/Unlimotion.Test/SingleViewStartupUiTests.cs`, `src/Unlimotion.Test/PackageUpdateCompatibilityUiTests.cs`, `src/Unlimotion.Android/bin/Debug/net10.0-android/android-arm64/com.Kibnet.Unlimotion-Signed.apk` |
| EXEC | Post-EXEC code review | 0.90 | Нет блокирующих данных | Отдать review пользователю | Нет | Нет | Review не выявил блокирующих дефектов; остаточный риск только в неподтверждённом запуске на реальном устройстве | `src/Unlimotion.Android/MainActivity.cs`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.Test/SingleViewStartupUiTests.cs` |
| EXEC | Review-followup hardening | 0.92 | `adb` device всё ещё не виден | Финальный self-review и ответ | Нет | Да, пользователь попросил внести изменения | Deferred update replay теперь предпочитает текущий `MainWindowViewModel.Settings`, чтобы stale startup reference не перехватывал replay; tests/package повторно зелёные | `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.Test/SingleViewStartupUiTests.cs`, `src/Unlimotion.Android/bin/Debug/net10.0-android/android-arm64/com.Kibnet.Unlimotion-Signed.apk` |
| EXEC | Дополнительное hardening после запроса | 0.93 | `adb` device не появился после `adb kill-server/start-server` | Отдать итог | Нет | Да, пользователь попросил внести изменения | `ConfigureUpdateService` теперь заменяет startup settings reference на текущий main settings reference; targeted UI tests и Android arm64 package прошли повторно | `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.Test/SingleViewStartupUiTests.cs`, `src/Unlimotion.Test/PackageUpdateCompatibilityUiTests.cs`, `src/Unlimotion.Android/bin/Debug/net10.0-android/android-arm64/com.Kibnet.Unlimotion-Signed.apk` |
| EXEC | Rebase on main and device install attempt | 0.91 | Нужен release-signed APK или явное разрешение на uninstall/keep-data для debug APK | Обновить PR-ветку после rebase | Нет | Да, пользователь попросил rebase и проверку на подключённом телефоне | Rebase на `origin/main` выполнен; конфликт с periodic update checks разрешён через `RunAutomaticUpdateCheckAsync`; targeted UI tests и Android arm64 package прошли; `adb install -r` заблокирован signature mismatch с установленным `1.24.0`/169 | `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.Android/MainActivity.cs`, `src/Unlimotion.Test/SingleViewStartupUiTests.cs`, устройство `SM-F956B` |
| EXEC | Full reinstall and device smoke | 0.97 | Нет | Обновить PR и отдать итог | Нет | Да, пользователь попросил переустановить и проверить | Keep-data reinstall был заблокирован retained signature state; полный uninstall/install debug APK успешно поставил `1.24.1-local`/170, приложение запущено, process pid `32382`, focused `MainActivity`, crash buffer без `FATAL EXCEPTION`, `AndroidRuntime`, `JavaProxyThrowable`, `NullReferenceException` по пакету | Устройство `SM-F956B`, `src/Unlimotion.Android/bin/Debug/net10.0-android/android-arm64/com.Kibnet.Unlimotion-Signed.apk`, `specs/2026-05-12-android-startup-after-update.md` |
| EXEC | Android shared-storage Git sync validation | 0.96 | Нет | Обновить PR и отдать итог | Нет | Да, пользователь сообщил о возврате проблемы синхронизации и подключил телефон | На `SM-F956B` воспроизведён Android shared-storage ownership mismatch: repo `/storage/emulated/0/Projects/Unlimotion.Tasks` owned by old UID `10327`, current app UID `u0_a896`; `.gitconfig` уже содержал safe path, но libgit2 не видел global config. Android bootstrap теперь явно задаёт `GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, [dataDir])`; для HTTPS fallback добавлен system CA directory. Debug APK `1.24.1-local-safegit2`/172 установлен поверх, manual pull и sync завершились без ownership/certificate/Git errors | `src/Unlimotion.Android/MainActivity.cs`, устройство `SM-F956B`, `artifacts/android-debug-arm64-package-safegit2.log`, `specs/2026-05-12-android-startup-after-update.md` |
