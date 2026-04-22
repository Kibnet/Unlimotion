# Локализация интерфейса и сообщений

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`; контекст `testing-dotnet`
- Владелец: Codex
- Масштаб: large
- Целевой релиз / ветка: текущая ветка Unlimotion
- Ограничения: на фазе реализации не менять формат task-файлов, не менять смысл пользовательских сценариев, не ломать persisted настройки; runtime-переключение языка не должно выполнять долгие операции в UI-потоке
- Связанные ссылки: запрос пользователя от 2026-04-21; official Avalonia docs: https://docs.avaloniaui.net/docs/guides/implementation-guides/localizing

## 1. Overview / Цель
Добавить настройку языка интерфейса с поддержкой минимум русского и английского, выбирать язык системы по умолчанию и использовать английский как fallback для неподдерживаемых культур. Все пользовательские строки desktop-интерфейса, статусы, уведомления, подтверждения и ошибки должны перейти на локализованные ресурсы, чтобы новые языки добавлялись отдельными файлами ресурсов.

## 2. Текущее состояние (AS-IS)
- Desktop UI построен на Avalonia 11.3.7: XAML живёт в `src/Unlimotion/Views/*.axaml`, приложение и команды в `src/Unlimotion/App.axaml.cs`.
- ViewModel слой находится в `src/Unlimotion.ViewModel`.
- Настройки внешнего вида уже хранятся в секции `Appearance` через `AppearanceSettings` и `SettingsViewModel`.
- Строки интерфейса сейчас захардкожены в XAML:
  - `SettingsControl.axaml`: настройки, хранилище, Git backup, SSH, advanced/service actions.
  - `MainControl.axaml`: табы, фильтры, редактор задачи, relation blocks, date/duration menus.
  - `MainScreen.axaml`: кнопки `Yes`/`No` в диалоге подтверждения.
  - `GraphControl.axaml` и `SearchBar.axaml`: фильтры и watermark поиска.
- Пользовательские сообщения сейчас захардкожены в C#:
  - `App.axaml.cs`: toast-уведомления, confirmation dialog text, folder picker title, Git operation statuses.
  - `SettingsViewModel.cs`: status text, onboarding hint, auth mode text.
  - `MainWindowViewModel.cs`, `TaskRelationPickerViewModel.cs`, `MainControl.axaml.cs`: delete confirmations, relation errors, drag-and-drop errors.
  - `BackupViaGitService.cs`, `FileStorage.cs`: ошибки, которые могут попадать пользователю через toast/exception message.
- Часть текущего UI уже на английском (`All Tasks`, `Completed`, `Search`, `Remove task`), часть на русском (`Настройки`, Git-status texts), поэтому сейчас нет единого языка интерфейса.
- Списки с display text часто совмещают persisted key и текст UI:
  - `SortDefinition.Name` отображается через `ToString()` и также сохраняется в конфиге.
  - `DateFilter.CurrentOption` использует строковый ключ из `Options.DateFilterOptions`.
  - `UnlockedTimeFilter.Title` и `DurationFilter.Title` являются одновременно model text и UI text.

## 3. Проблема
Пользовательские строки разбросаны по XAML и ViewModel/service коду, не имеют единого источника переводов и местами смешивают английский с русским, поэтому нельзя корректно выбрать язык системы, переключить язык в настройках и безопасно добавлять новые языки отдельными файлами.

## 4. Цели дизайна
- Разделение ответственности: ViewModel и сервисы получают строки через локализационный сервис, XAML получает строки через binding/markup extension, а файлы переводов остаются отдельными ресурсами.
- Повторное использование: один набор resource keys используется для XAML, ViewModel status text, уведомлений и диалогов.
- Тестируемость: resolution языка, fallback, полнота ключей и ViewModel-тексты покрываются автоматическими тестами.
- Консистентность: весь desktop UI отображается на одном выбранном языке.
- Обратная совместимость: существующие persisted настройки темы, размера текста, сортировки и фильтров не ломаются при смене языка.
- Расширяемость: новый язык добавляется новым `.resx` файлом и регистрацией culture metadata без переписывания UI.

## 5. Non-Goals (чего НЕ делаем)
- Не локализуем пользовательские данные: названия задач, описания задач, emoji/title task content и пути файлов.
- Не переводим README, specs, comments, logs для разработчиков и внутренние diagnostic messages, которые не показываются пользователю.
- Не меняем server/browser/telegram UI, если строка не используется desktop-клиентом.
- Не добавляем машинный перевод или runtime-загрузку переводов из сети.
- Не меняем бизнес-логику Git backup, storage, relation picker и сортировок кроме отделения stable keys от display text.
- Не требуем перезапуска приложения для смены языка, если runtime-обновление можно сделать без несоразмерного риска.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.ViewModel/Localization/*.cs` -> локализационный сервис, список поддерживаемых языков, resolution persisted/system/fallback culture, formatting helpers.
- `src/Unlimotion.ViewModel/Resources/Strings.resx` -> английский fallback/default resource file.
- `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` -> русский resource file.
- `SettingsViewModel.cs` -> persisted настройка `Appearance:Language`, список вариантов языка, selected index/value, refresh локализованных status texts при смене языка.
- `App.axaml.cs` -> применить язык до загрузки XAML/resources и подписаться на изменения языка для runtime-refresh.
- `Views/*.axaml` -> заменить hardcoded UI text на локализационный binding/markup extension.
- `MainWindowViewModel.cs`, `TaskRelationPickerViewModel.cs`, `MainControl.axaml.cs`, `BackupViaGitService.cs`, `FileStorage.cs` -> заменить пользовательские строки на resource keys.
- `SortDefinition`, `DateFilter`, `DurationFilter`, `UnlockedTimeFilter`, relation picker option models -> отделить stable id от локализованного display text.
- `Unlimotion.Test` -> добавить tests для language resolution/fallback/resource completeness/ViewModel localized strings.

### 6.2 Детальный дизайн
- Использовать стандартный .NET `.resx` + `ResourceManager` подход. Это соответствует официальной Avalonia-документации по ResX localization; поверх него добавить небольшой runtime layer, потому что простой `{x:Static}` не обновляет уже созданные controls при смене языка.
- Default resource file: `Strings.resx` на английском. Это fallback для `ResourceManager` и явный fallback по product rule.
- Русский ресурс: `Strings.ru.resx`. Для `ru-RU`, `ru-BY`, `ru-KZ` и других `ru-*` выбирается neutral culture `ru`.
- Поддерживаемые language modes:
  - `System` / persisted `"System"`: значение по умолчанию, разрешается из captured OS/startup UI culture provider.
  - `en` / English.
  - `ru` / Русский.
- Если system culture не начинается с `ru` и не начинается с `en`, effective culture становится `en`.
- Persisted настройка хранится в `Appearance:Language`; пустое/отсутствующее значение трактуется как `"System"`.
- При применении языка сервис выставляет:
  - `CultureInfo.DefaultThreadCurrentUICulture`;
  - `CultureInfo.DefaultThreadCurrentCulture`;
  - `Thread.CurrentThread.CurrentUICulture`;
  - `Thread.CurrentThread.CurrentCulture`.
- Разрешение режима `System` не должно читать уже изменённый `CultureInfo.CurrentUICulture` после ручного переключения языка. Нужен отдельный источник системной культуры:
  - `ILocalizationSystemCultureProvider` или equivalent abstraction;
  - default implementation фиксирует OS/startup UI culture до первого `SetLanguage`;
  - tests могут подставлять `ru-RU`, `en-US`, `de-DE` без изменения process-global culture.
- Persisted/config parsing and serialization must remain culture-safe. Смена `CurrentCulture` допустима для UI formatting, но чтение/запись persisted numeric/date values должно использовать текущие библиотеки безопасно либо явно `InvariantCulture` там, где код парсит/форматирует строки вручную. В рамках EXEC нужно проверить, что `Appearance:FontSize`, Git intervals and duration input не начинают сериализоваться в culture-specific format.
- Для XAML добавить lightweight markup extension/binding, например `Text="{loc:Localize SettingsTitle}"`, который читает `LocalizationManager.Instance["SettingsTitle"]` и обновляется при `PropertyChanged`/culture change.
- Для C# добавить `ILocalizationService`/static facade:
  - `string Get(string key)`;
  - `string Format(string key, params object[] args)`;
  - `CultureInfo CurrentCulture`;
  - `IReadOnlyList<LanguageOption> SupportedLanguages`;
  - событие/observable `CultureChanged`.
- ViewModel не должна зависеть от Avalonia. Если нужен XAML markup extension, он может жить в UI-проекте и ссылаться на ViewModel localization service.
- Для строк с параметрами использовать resource placeholders, например `StorageConnectedAs = Connected as {0}.`, `RemoveTaskMessage = Are you sure you want to remove the task "{0}" from disk?`.
- Для DateTime/TimeSpan display использовать текущую culture там, где формат не является domain-specific. Форматы task metadata `yyyy.MM.dd` можно оставить как продуктовый compact format, но локализовать подписи `Created`, `Updated`, `Unlocked`, `Completed`, `Archived`.
- При смене языка:
  - XAML localized bindings обновляются автоматически.
  - `SettingsViewModel` пересчитывает `StorageStatusText`, `BackupStatusText`, `GitBackupOnboardingHint`, `BackupAuthModeText`.
  - `MainWindowViewModel` пересоздаёт или invalidates display collections для sort/date/duration/unlocked filter titles без изменения stable ids и persisted values.
  - already open confirmation dialogs допускается не обновлять на лету; новые диалоги должны использовать новый язык.
- Runtime-refresh acceptance scope для "основного UI" включает:
  - tab headers;
  - toolbar filters and search watermarks;
  - active task editor labels, watermarks and menu items;
  - graph filters/search watermark;
  - settings page labels, hints, language names, status texts and onboarding hint;
  - relation section labels, picker buttons and picker watermarks;
  - sort/date/duration/unlocked filter display names in visible ComboBox/CheckBox items after collection refresh.
  Already open confirmation/toast content may remain in the language used when it was created; newly created dialogs/toasts must use the current language.

## 7. Бизнес-правила / Алгоритмы
| Сценарий | Условие | Поведение |
| --- | --- | --- |
| Первый запуск на русской системе | `Appearance:Language` пустой, `CurrentUICulture = ru-RU` | Effective language `ru`, UI на русском |
| Первый запуск на английской системе | `Appearance:Language` пустой, `CurrentUICulture = en-US` | Effective language `en`, UI на английском |
| Первый запуск на неподдерживаемой системе | `Appearance:Language` пустой, `CurrentUICulture = de-DE` | Effective language `en` |
| Язык выбран вручную | `Appearance:Language = ru` или `en` | Выбранный язык имеет приоритет над system culture |
| Возврат к системному языку | пользователь выбирает `System` | Persisted value `"System"`, effective culture снова считается из captured OS/startup system culture |
| Отсутствует ключ в `Strings.ru.resx` | ключ есть в `Strings.resx` | Runtime fallback отдаёт английскую строку как safety net; parity test fails and blocks completion |
| Сортировка сохранена до локализации | old config содержит `Created Ascending` | Stable id/compat mapping находит прежнюю сортировку, display text локализован |
| Date filter сохранён до локализации | `CurrentOption = Today` | `Today` остаётся stable id, display text локализован |
| Пользовательский task title | task title содержит любой язык | Не переводится |
| Внешняя ошибка | `ex.Message` пришёл из OS/Git/libgit2/.NET | Локализуется префикс/action context; raw external detail may remain as returned by dependency |

## 8. Точки интеграции и триггеры
- `App.Init(configPath)` после создания configuration и до создания `SettingsViewModel` применяет persisted/system language, используя captured OS/startup culture provider для режима `System`.
- `App.Initialize()` загружает XAML уже с установленной culture.
- `SettingsViewModel.LanguageModeIndex` или аналогичный setter сохраняет `Appearance:Language` и вызывает `LocalizationService.SetLanguage(...)`.
- `LocalizationService.CultureChanged` является триггером для:
  - обновления XAML localized bindings;
  - пересчёта status/hint properties в `SettingsViewModel`;
  - обновления локализованных display names в списках фильтров/сортировок.
- Все toast/Ask/error messages формируются непосредственно перед показом через локализационный сервис, чтобы использовать актуальный язык.

## 9. Изменения модели данных / состояния
- Добавляется persisted поле `Appearance:Language`.
- Значение по умолчанию: отсутствует или `"System"`.
- Existing config остаётся валидным; миграция не требуется.
- Для локализуемых списков добавляются stable ids/resource keys:
  - `SortDefinition.Id`, `SortDefinition.ResourceKey`;
  - date filter option id/resource key вместо display string как единственного ключа;
  - `DurationFilter.ResourceKey`, `UnlockedTimeFilter.ResourceKey`;
  - relation picker labels/resource keys.
- Старые persisted sort/date values должны приниматься как compatibility ids, чтобы пользовательские настройки фильтров/сортировки не сбросились.

## 10. Миграция / Rollout / Rollback
- При первом запуске после изменения `Appearance:Language` может отсутствовать; это нормальный state, трактуется как `System`.
- При выборе языка настройка записывается в конфиг тем же WritableJsonConfiguration механизмом, что тема и размер текста.
- Старые persisted sort names остаются stable ids или мапятся на новые ids.
- Rollback:
  - удалить `Appearance:Language` из UI usage, но не обязательно чистить persisted value;
  - вернуть hardcoded XAML/C# strings;
  - удалить resource files и localization tests вместе с rollback.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В настройках есть поле выбора языка с вариантами `System`, `English`, `Русский` или локализованными эквивалентами.
  - По умолчанию используется язык системы, если он поддержан.
  - Для неподдерживаемого системного языка effective language становится английским.
  - Ручной выбор русского/английского сохраняется в `Appearance:Language` и применяется без перезапуска к runtime-refresh scope из раздела 6.2.
  - После ручного выбора языка и последующего выбора `System` effective language снова считается из captured OS/startup culture, а не из ранее выбранной process culture.
  - Persisted/config values для font size, Git intervals and duration-related settings не меняют формат из-за выбранной UI culture.
  - `SettingsControl`, `MainControl`, `MainScreen`, `GraphControl`, `SearchBar` не содержат пользовательских hardcoded строк, кроме символов/иконок, binding text, persisted ids и технических examples.
  - Toast-уведомления, confirmation dialogs, status texts, folder picker title и пользовательские errors локализованы; для внешних `ex.Message` локализуется context/prefix, raw dependency detail может остаться как есть.
  - Resource files `Strings.resx` и `Strings.ru.resx` содержат одинаковый набор ключей; parity failure blocks completion.
  - Новые языки можно добавить отдельным `Strings.<culture>.resx` файлом и записью в supported languages metadata.
- Какие тесты добавить/изменить:
  - `LocalizationSettingsTests`: `System` resolves to `ru` for `ru-RU`.
  - `LocalizationSettingsTests`: unsupported culture resolves to `en`.
  - `LocalizationSettingsTests`: manual `ru`/`en` overrides system culture.
  - `LocalizationSettingsTests`: switching from manual language back to `System` uses captured system culture, not currently selected process culture.
  - `LocalizationResourceTests`: key parity между английским и русским `.resx`.
  - `LocalizationResourceTests`: resource lookup falls back to English only as runtime safety net; missing translated keys remain test failures.
  - `SettingsViewModelTests`: выбор языка persists в `Appearance:Language` и обновляет локализованные settings/status strings.
  - `SettingsViewModelTests`/новые tests: `GitBackupOnboardingHint`, `BackupAuthModeText`, `StorageStatusText`, `BackupStatusText` выдаются на выбранном языке.
  - `MainWindowViewModelTests`: stable sort/date/filter ids не ломаются при localized display text.
  - Regression tests: `Appearance:FontSize` and Git interval settings keep culture-safe persisted values after switching UI language.
  - Compile/build validation для XAML bindings.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln --no-build`
  - `rg -n 'Text=\"[A-Za-zА-Яа-яЁё]|Content=\"[A-Za-zА-Яа-яЁё]|Header=\"[A-Za-zА-Яа-яЁё]|Watermark=\"[A-Za-zА-Яа-яЁё]' src/Unlimotion/Views -g '*.axaml'`
  - `rg -n '[А-Яа-яЁё]' src/Unlimotion src/Unlimotion.ViewModel -g '*.cs' -g '*.axaml'`
  - `rg -n '\"[^\"\\r\\n]*(Remove task|Task storage is not configured|Are you sure|Search|Settings|Completed|Archived|Wanted|Created|Updated|Unlocked|Archive|Add|Cancel|From|To)[^\"\\r\\n]*\"' src/Unlimotion src/Unlimotion.ViewModel -g '*.cs'`

## 12. Риски и edge cases
- Масштаб большой: интерфейсные строки находятся во многих XAML/C# файлах; нужен staged conversion с targeted tests.
- Runtime-переключение языка может не обновить уже открытый confirmation dialog; это допустимо, если новые диалоги открываются на новом языке.
- `SortDefinition.Name` и date filter strings сейчас используются как persisted keys; прямой перевод этих строк сломает сохранённые настройки, поэтому нужны stable ids.
- `.resx` designer visibility: для XAML/static access official docs требуют public generated resources; если используется custom service с `ResourceManager`, важно правильно указать namespace/base name.
- Проверка `rg` по строкам даст false positives на comments, persisted ids, branch names, examples (`id_ed25519_unlimotion`, `refs/heads/main`) и symbols. Их нужно классифицировать, а не механически удалять.
- English-only C# literals труднее отфильтровать, чем русские строки. EXEC должен вести allowlist для technical constants/resource keys/test data и отдельно исправлять user-facing literals.
- Некоторые service exception messages могут попадать в тестовые assertions; тесты нужно обновлять на resource-backed messages без потери проверки смысла.
- Смена `CurrentCulture` может повлиять на формат дат/чисел and numeric serialization. Product-specific and persisted formats должны быть явно зафиксированы там, где важна стабильность; external exception details are not fully localizable.

## 13. План выполнения
1. Добавить localization infrastructure в ViewModel/UI: resource files, service, language resolution, XAML markup/binding.
2. Добавить tests для culture resolution, captured system culture fallback, resource key parity и persisted `Appearance:Language`.
3. Встроить language setting в `SettingsViewModel` и `SettingsControl`; применить язык при `App.Init`.
4. Перевести `SettingsControl.axaml` и settings/status/toast strings из `SettingsViewModel`/`App.axaml.cs`.
5. Перевести main UI XAML: `MainScreen`, `MainControl`, `GraphControl`, `SearchBar`.
6. Отделить stable ids от display text для sort/date/duration/unlocked filters и relation picker labels.
7. Перевести пользовательские dialogs/errors в `MainWindowViewModel`, `TaskRelationPickerViewModel`, `MainControl.axaml.cs`, `BackupViaGitService`, `FileStorage`.
8. Запустить targeted tests, `dotnet build`, полный `dotnet test`.
9. Запустить `rg`-проверки hardcoded строк, включая C# English user-facing literals, классифицировать остатки через allowlist и исправить пользовательские строки.
10. Выполнить post-EXEC review: проверить отклонения от спеки, missing tests, stale comments и fallback behavior.

## 14. Открытые вопросы
Нет блокирующих вопросов. Принятые допущения:
- Desktop UI является целевой областью фразы "весь интерфейс и все сообщения".
- Browser/Telegram/Server UI и developer-only logs не входят в текущую задачу.
- В настройке языка нужен вариант `System`, чтобы пользователь мог вернуться к автоматическому выбору языка системы.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - UI-поток не блокируется: смена языка выполняет только resource lookup/property notifications.
  - Изменения XAML bindings сохраняют существующие automation-id/test selectors.
  - ViewModel остаётся без Avalonia-зависимости; Avalonia-specific markup extension живёт в UI-проекте.
  - План проверки включает `dotnet build` и `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | английские строки | Fallback/default language |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | русские строки | Russian UI |
| `src/Unlimotion.ViewModel/Localization/*` | service, settings, supported language metadata | Единая логика локализации |
| `src/Unlimotion/Localization/*` | Avalonia markup extension/binding adapter, если нужен | Runtime localization in XAML |
| `src/Unlimotion.ViewModel/AppearanceSettings.cs` | language key constants / parse helpers | Persisted language setting |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | language options, selected language, localized status/hints | Settings UI and state |
| `src/Unlimotion/App.axaml.cs` | apply language on startup, localized command messages | Startup and notifications |
| `src/Unlimotion/Views/SettingsControl.axaml` | language selector and localized text bindings | Settings UI |
| `src/Unlimotion/Views/MainControl.axaml` | localized tabs/filter/editor/relation texts | Main UI |
| `src/Unlimotion/Views/MainScreen.axaml` | localized dialog buttons | Confirmation dialogs |
| `src/Unlimotion/Views/GraphControl.axaml` | localized filters/search watermark | Graph UI |
| `src/Unlimotion/Views/SearchControl/SearchBar.axaml` | localized search watermark | Search UI |
| `src/Unlimotion.ViewModel/SortDefinition.cs` | stable id + localized display | Preserve sort settings while localizing |
| `src/Unlimotion.ViewModel/DateFilter.cs`, `Options.cs` | stable date option ids + localized display | Preserve date filters |
| `src/Unlimotion.ViewModel/DurationFilter.cs`, `UnlockedTimeFilter.cs` | resource keys for titles | Localized filters |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | localized dialogs/errors/filter titles | Main messages |
| `src/Unlimotion.ViewModel/TaskRelationPickerViewModel.cs` | localized relation picker errors/labels | Relation messages |
| `src/Unlimotion/Views/MainControl.axaml.cs` | localized drag-and-drop errors | UI error messages |
| `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion/FileStorage.cs` | localized user-facing errors | Service messages shown to users |
| `src/Unlimotion.Test/*` | localization tests and assertion updates | Behavioral safety |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Язык UI | смешанные hardcoded RU/EN строки | единый selected/effective language |
| Default language | не определён | system language, fallback English |
| System mode source | mutable current UI culture | captured OS/startup culture provider |
| Добавление языка | поиск и правка XAML/C# строк | новый `Strings.<culture>.resx` + metadata |
| XAML text | hardcoded `Text`, `Content`, `Header`, `Watermark` | localization binding/markup extension |
| ViewModel messages | hardcoded string literals | `ILocalizationService.Get/Format` |
| Sort/date/filter display | display string часто является persisted key | stable id + localized display |
| Missing Russian key | runtime мог бы показать key или старую строку | English fallback + parity test catches missing key |
| External exception details | весь текст считается локализуемым | localized prefix/context + raw dependency detail if needed |

## 18. Альтернативы и компромиссы
- Вариант: использовать только Avalonia resource dictionaries (`Lang/ru.axaml`, `Lang/en.axaml`).
- Плюсы: удобно для XAML и `DynamicResource`.
- Минусы: ViewModel/service сообщения получают второй источник строк или Avalonia dependency; сложнее обеспечить единый набор ключей.
- Почему выбранное решение лучше: `.resx`/`ResourceManager` является стандартным .NET механизмом, поддерживает fallback culture chain и одинаково доступен из ViewModel, services и XAML adapter.

- Вариант: добавить внешний NuGet localization package.
- Плюсы: меньше собственного glue-code.
- Минусы: новая зависимость для базового UI-механизма, неизвестная совместимость с текущими compiled bindings и net10/Avalonia setup.
- Почему выбранное решение лучше: небольшой локальный слой поверх стандартного ResourceManager снижает dependency risk и проще тестируется.

- Вариант: применять новый язык только после перезапуска.
- Плюсы: проще реализация.
- Минусы: настройка языка выглядит неполной и ухудшает UX.
- Почему выбранное решение лучше: runtime update достижим без тяжёлых операций; это ожидаемое поведение для языковой настройки.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, runtime flow, fallback, state и rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Persisted compatibility, rollback и risks описаны. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, тесты и команды проверки указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План пошаговый, блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | Требования `dotnet-desktop-client` отражены. |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Scope desktop localization and Non-Goals explicit. |
| 2. Понимание текущего состояния | 5 | Перечислены конкретные XAML/C# hotspots and persisted string risks. |
| 3. Конкретность целевого дизайна | 5 | Описаны `.resx`, service, XAML adapter, language setting and fallback algorithm. |
| 4. Безопасность (миграция, откат) | 5 | Existing config compatibility and rollback path described. |
| 5. Тестируемость | 5 | Есть unit, resource parity, ViewModel and build/rg checks. |
| 6. Готовность к автономной реализации | 5 | Open blockers absent, plan and file table concrete. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: после review явно добавлены риски persisted display strings для sort/date filters, runtime-refresh граница для already open dialogs, resource parity test, `System` language option, scope exclusion для Browser/Telegram/Server UI и developer-only logs. По дополнительному review добавлены captured OS/startup culture provider для режима `System`, C# English literal audit, culture-safe persisted/config values, конкретный runtime-refresh scope, blocking resource parity gate and exception boundary для внешних `ex.Message`.
- Что осталось на решение пользователя: требуется подтверждение спеки фразой `Спеку подтверждаю`.

## Approval
Подтверждено пользователем фразой: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор instruction stack и AS-IS локализации | 0.9 | Нет | Создать рабочую спецификацию | Да | Нет | Центральный QUEST-гейт требует SPEC-first; осмотрены XAML/ViewModel hotspots и официальный Avalonia ResX approach | `specs/2026-04-21-ui-localization.md` |
| SPEC | Создание и post-SPEC review | 0.9 | Нет | Ожидать фразу `Спеку подтверждаю` | Да | Да, требуется подтверждение перед EXEC | Спека прошла linter/rubric; блокирующих вопросов нет | `specs/2026-04-21-ui-localization.md` |
| SPEC | Исправления по review пользователя | 0.95 | Нет | Ожидать фразу `Спеку подтверждаю` | Да | Да, пользователь запросил внести исправления | Уточнены 6 review-находок: system culture source, C# English audit, invariant persistence, runtime-refresh scope, missing-key gate, external exception boundary | `specs/2026-04-21-ui-localization.md` |
| EXEC | Старт реализации после approval | 0.9 | Нет | Добавить regression tests и localization infrastructure | Нет | Да, пользователь подтвердил спеку | Фраза `Спеку подтверждаю` переводит QUEST в EXEC; реализация ограничена утверждённой спецификацией | `specs/2026-04-21-ui-localization.md` |
| EXEC | Локализационная инфраструктура и первичная замена UI/messages | 0.85 | Нет | Запустить тесты и аудит hardcoded строк | Нет | Нет | Добавлены `.resx` EN/RU, localization service, настройка языка, runtime resources, stable display ids для фильтров/сортировок/repeater и локализация основных XAML/C# сообщений; сборка `Unlimotion.Test.csproj` прошла без ошибок | `src/Unlimotion.ViewModel/Resources/*`, `src/Unlimotion.ViewModel/Localization/*`, `src/Unlimotion/Views/*`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.ViewModel/*`, `src/Unlimotion.Test/*` |
| EXEC | Финальная проверка и post-EXEC review | 0.9 | Нет | Передать итог пользователю | Нет | Нет | `dotnet build src\Unlimotion.sln` и `dotnet test src\Unlimotion.sln --no-build` прошли; audit hardcoded строк оставил только DynamicResource/binding false positives, technical examples, comments, persisted ids/report/debug strings вне desktop UI scope | `src/Unlimotion.sln`, `src/Unlimotion.Test/*`, `specs/2026-04-21-ui-localization.md` |
| EXEC | Регрессия при переключении языка | 0.9 | Нет | Передать итог пользователю | Нет | Да, пользователь сообщил exception при смене языка | Найден UI transient state: при пересоздании локализованных списков selection может стать `null`/`-1`; добавлены guards для sort/date filters/language index и regression tests; `dotnet build src\Unlimotion.sln /m:1 /nodeReuse:false /p:UseSharedCompilation=false /clp:ErrorsOnly` и `dotnet test src\Unlimotion.sln --no-build` прошли | `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion.Test/LocalizationDisplayDefinitionTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `specs/2026-04-21-ui-localization.md` |
