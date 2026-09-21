# Защита Settings.json от обнуления и безопасный запуск

## 0. Метаданные

- Статус: EXEC завершён / PASS. Пользователь подтвердил исходную SPEC «Спеку подтверждаю» и принял процедурное отклонение AC3 ответом «Принимаю» 2026-09-21. Последующим отдельным поручением опубликована библиотека `WritableJsonConfiguration`; после Windows CI portability finding актуальная исправленная версия — 8.1.2. Unlimotion переведён с локального prerelease-пакета на публичную версию 8.1.2. Установленная версия приложения и пользовательские настройки не изменялись.
- Профиль: `dotnet-desktop-client`; дополнения `testing-dotnet`, `ui-automation-testing`.
- Форма: expanded по центральному `_template.md`, поскольку затронуто сохранение конфигурации. Масштаб medium; риск потери настроек требует проверки даже при небольшом коде.
- Владелец реализации: основной агент. Пользователь определил приоритет: «без оверинжиниринга».
- Первая версия: установленное Windows desktop-приложение. Android, Browser, Linux и macOS сохраняют текущий режим; перенос защиты на них не входит в это исправление.
- Instruction stack: central `AGENTS.md`, routing-matrix, creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration-baseline, quest-governance, quest-mode, testing-baseline, spec-linter, spec-rubric, review-loops; локальный `AGENTS.override.md`.
- Поверхность: Codex desktop / Windows / PowerShell; sandbox unrestricted, approval never. Точный model ID и reasoning не проверялись и не используются как evidence. Model eval: не применимо, поведение модели не меняется.
- Проверенный checkout: `d4d90de7`, detached HEAD, чистый до создания этой спеки. Это исходники v1.31.0, а не заявление об актуальности remote main.
- EXEC preflight: ветка `fix/settings-file-crash-safety`, fast-forward на проверенный `origin/main` (`53dacb06`); App/TestHost изменения main просмотрены и сохранены. Библиотека клонирована рядом в `../WritableJsonConfiguration`, baseline `aa75067`; локальных AGENTS/overrides в ней нет, provider совпадает с проверенным 8.0.1.
- Ownership EXEC: основной агент — все изменения Unlimotion и интеграция; отдельный worker — только checkout WritableJsonConfiguration и его тесты/локальный nupkg. Сборки с общими output directories не выполняются одновременно. Финальный Debug main runtime (`--no-build`) шёл параллельно последовательной цепочке Release cross-target builds; их outputs разделены. Native UI и замер старта выполнялись отдельно от сборок.
- В EXEC: рабочая ветка `fix/settings-file-crash-safety`; перед изменениями повторить preflight и сверить изменения основной ветки в затронутых файлах.
- Второй исходный репозиторий: `https://github.com/Kibnet/WritableJsonConfiguration`; минимальный фикс библиотеки входит в предлагаемый scope. Его checkout создаётся отдельно только в EXEC, после чтения его инструкций.
- Публикация NuGet, push/PR, выпуск и установка Unlimotion, восстановление настоящего пользовательского Settings.json — отдельные действия, не разрешённые этой SPEC.

## 1. Overview / Цель

Настройки не должны превращаться в пустой или обрезанный файл при прерывании сохранения. При уже повреждённом файле приложение восстанавливает предыдущую читаемую копию либо показывает понятную ошибку вместо исчезновения при запуске.

Outcome contract:

- Исходный симптом: установленная Windows-версия не открывается из-за нулевого `Settings.json`. Последнее поручение: подготовить спеку простого исправления.
- Success means: при прерывании нашей записи остаётся читаемая старая или новая конфигурация; при повреждении используется проверенный `.bak`; без него нет автоматического сброса и подключения к случайному пространству задач.
- Выход SPEC: этот документ с решениями, тестами и review. Выход EXEC: небольшой фикс провайдера, подключение в приложении, recovery UI и доказательства проверок на искусственных данных.
- Stop rules: на SPEC меняется только этот файл; на EXEC — только утверждённый scope. Новая БД, массовая архитектурная переделка и публикация не являются продолжением этого исправления.

## 2. Текущее состояние (AS-IS)

- Диагностика 18.09.2026: пять `.NET Runtime` событий 1026; `Settings.json` — 0 байт, ошибка JSON до появления окна. Это историческое evidence из данной задачи; причина обнуления конкретным процессом не доказана.
- `src/Unlimotion/App.axaml.cs:2355`: `WritableJsonConfigurationFabric.Create(configPath, reloadOnChange: false)`. Остальные ошибки инициализации повторно выбрасываются.
- `src/Unlimotion.Desktop/Program.cs` определяет пользовательский путь и поддерживает `--config=`. Тесты могут работать с отдельной конфигурацией.
- Проверен исходный код библиотеки 8.0.1, commit `da8e26004c90d64027671f9583f1207b7afbe76b`: `Save` вызывает `File.WriteAllText`; оба `Set` делают read–modify–write без общей блокировки; `SetValue` меняет `Data` до успешной записи.
- Важное ограничение: `Set(string, object)` не virtual, `Save` private, расширение `.Set(...)` явно вызывает этот overload. Простое наследование провайдера не защищает все записи.
- `TaskSpaceSettingsPersistenceQueue` уже упорядочивает часть изменений. Другие `.Set(...)` выполняются напрямую. `TaskSourceSettingsAdapter` имеет существующий журнал логических изменений внутри JSON.
- В MainControl есть recovery overlay для повреждения каталога пространств, но его ViewModel требует уже загруженную конфигурацию. Пустой JSON требует более раннего, простого экрана.
- Android и Browser предварительно создают `{}` при отсутствии main до `App.Init`; это несовместимо с новым missing-main recovery. Поэтому в этой первой версии новый режим включается только на Windows, без изменения остальных bootstrap-путей.
- `Settings.json` может содержать секреты. Содержимое не выводится в логи, тестовые артефакты и отчёты.

Первичные источники:

- [Провайдер 8.0.1](https://github.com/Kibnet/WritableJsonConfiguration/blob/da8e26004c90d64027671f9583f1207b7afbe76b/src/WritableJsonConfiguration/WritableJsonConfigurationProvider.cs).
- [Расширения Set](https://github.com/Kibnet/WritableJsonConfiguration/blob/da8e26004c90d64027671f9583f1207b7afbe76b/src/WritableJsonConfiguration/WritableJsonConfigurationProviderExtensions.cs).
- [File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-10.0): замена с резервной копией; файлы должны находиться на одном томе.
- [FileStream.Flush(Boolean)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0): сброс промежуточных буферов. Это не гарантия от любого аппаратного отказа.

## 3. Проблема

Перезапись единственной копии настроек разрушает предыдущую версию до завершения новой, а загрузчик не предлагает восстановления после повреждения.

## 4. Цели дизайна

- Сохранить `IConfiguration`, существующие `.Set(...)`, формат JSON и пользовательские пути.
- Исправить место физической записи; не переносить все настройки в новый сервис.
- Использовать одну резервную копию и простые файловые операции.
- Проверять исходный сценарий падения и сохранность реального содержимого на синтетических данных.

## 5. Non-Goals

- Новый `ISettingsStore`, новая общая async-очередь, event bus, SQLite, WAL, история из нескольких поколений.
- Изменение семантики слияния объектов/массивов `.Set`, миграция структуры настроек, переписывание существующего task-space journal.
- Полная транзакционность серии разных `.Set(...)`; сохранение каждого ещё не записанного UI-изменения при принудительном завершении.
- Координация нескольких процессов, named mutex, CAS/hash-протокол, live merge с внешним редактором. Они не нужны для устранения обнуления собственным writer; конкурентная потеря отдельных изменений между процессами остаётся известным ограничением.
- Мастер импорта/сброса, выбор пространства задач на recovery-экране, автоматическая замена повреждения на `{}`.
- Новая подсистема логирования, вынос секретов в keychain, автоматические внешние бэкапы.
- Переделка updater/shutdown. Атомарная запись защищает целостность файла даже при внезапном завершении; гарантии сохранения всей pending-очереди — отдельная задача.
- Поддержка нового режима на Android/Browser/Linux/macOS и single-view recovery. Это отдельное расширение после проверки особенностей их файловых систем и bootstrap.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

1. `WritableJsonConfiguration`: небольшой opt-in режим безопасной физической записи в существующем provider, без нового публичного сервиса.
2. `Unlimotion/Services/SettingsFileRecovery.cs`: проверка основного файла и `.bak` перед созданием runtime; результат normal / restored / blocked.
3. `App.axaml.cs`: включение режима, ранний recovery flow, уведомление о восстановлении.
4. `Views/SettingsRecoveryView`: один простой экран для Windows desktop shell; без task repository и фоновых jobs.

### 6.2 Детальный дизайн

**Подключение библиотеки.** Добавить в `WritableJsonConfigurationSource` один параметр `UseAtomicWrites`, по умолчанию false. В общем App только ветка `OperatingSystem.IsWindows()` включает новый startup flow и provider: подготовить source с Path, Optional и ReloadOnChange=false, разрешить FileProvider, выполнить recovery и построить root с этим же source (см. ниже). Остальные платформы используют прежний factory-вызов без recovery/opt-in. Настройку-флаг для пользователя не вводить. Существующие потребители server/bot сохраняют прежний режим. TFM библиотеки `netstandard2.0` сохраняется; opt-in контракт первой версии поддерживает Windows, при явном opt-in на другой ОС библиотека возвращает понятный PlatformNotSupportedException до записи, а legacy-режим остаётся доступен.

Такой путь предпочтительнее локального форка API внутри ViewModel: нет дублирующих extension methods, неоднозначности `.Set(...)`, reflection-patch или новых зависимостей между UI-модулями. Исправление исходников библиотеки выполняется в отдельном checkout; локальный пакет проверяется через уже существующий `artifacts/nuget-local`. Локальная версия уникальна, установленная 8.0.1 не подменяется.

**Единый путь.** До recovery подготовить configuration source и builder тем же способом, что текущая factory (`ResolveFileProvider` / `EnsureDefaults`), но ещё не загружать root. Передать helper именно `source.FileProvider.GetFileInfo(source.Path).PhysicalPath`; затем загрузить root с тем же source. Все main/bak/temp/corrupt операции и ключ блокировки используют этот resolved path. Не вычислять путь второй раз через cwd. В частности, относительные `Settings.json` / `--config=...` сохраняют существующее разрешение относительно provider/AppContext.BaseDirectory. При отсутствии physical path — явная ошибка, без guessed fallback.

**Права копий.** На Windows до записи конфиденциальных байтов temp, bootstrap-bak и corrupt-копии применить права доступа, не шире прав источника данных: main для save/quarantine, bak для restore. Простого наследования ACL каталога недостаточно; при невозможности сохранить ограничение вернуть ошибку до записи байтов. `File.Replace` не должен расширить права существующих main/bak; если права источника и назначения различаются и их ограничения нельзя сохранить, вернуть ошибку без ослабления доступа. Для настоящего first-run без файлов — права пользовательского каталога. Это узкая часть файлового helper; хранилище секретов, управление системными ACL и перенос permissions на другие ОС вне scope. Обязательная проверка Windows: restrictive main при более открытом parent directory, включая fault path и все оставшиеся temp/corrupt/bak; restore не расширяет доступ к данным restrictive bak.

**Сохранение в безопасном режиме.** Одна критическая секция на полный путь файла внутри процесса охватывает оба `Set` overload и read–modify–write целиком. Сравнение путей учитывает Windows case-insensitivity. Разные экземпляры провайдера одного пути используют ту же секцию. Multi-process lock не вводится.

1. Прочитать существующий JSON, собрать отдельный кандидат и отдельный candidate `Data`. При повреждении файла во время работы — ошибка сохранения; не восстанавливать и не затирать его фоновым `.Set`.
2. Сериализовать кандидат, проверить, что его принимает тот же JSON configuration parser, что используется при старте. Корневой объект обязателен; ключи, неизвестные приложению, сохраняются. Совместимость string/bool/number, null, вложенных объектов, массивов и существующего journal подтверждается characterization-тестами.
3. Если изменение действительно отсутствует — не писать файл и не вращать `.bak`.
4. Создать уникальный temp рядом с основным файлом через CreateNew; записать UTF-8, завершить writer/encoder, вызвать `Flush(true)` на файловом потоке.
5. Для существующего корректного main вызвать `File.Replace(temp, main, main + ".bak")`; для первого файла — `File.Move(temp, main)` без overwrite. Не удалять main перед переносом и не использовать fallback `Copy(..., overwrite: true)`.
6. Только после успешной публикации заменить provider `Data` подготовленным snapshot. Между публикацией и обновлением памяти нет новой сериализации или парсинга, способных превратить успешную запись в ошибку.
7. При неуспехе не публиковать candidate `Data`; сообщить ошибку через существующего вызывающего потребителя, очистить только свой незакоммиченный temp по возможности. Если сама replace-операция вернула неопределённый I/O-результат, не обещать неизменность main: перечитать/проверить фактическое состояние, остановить дальнейшие записи этого provider при невозможности согласовать его с диском. Сохранные main/bak не удалять.

`.bak` — предыдущий читаемый snapshot, не обязательно предыдущая законченная бизнес-операция. Внутренний task-space journal продолжает восстанавливать свои операции. Не утверждать, что атомарная запись файла автоматически делает серию Set транзакцией.

При отсутствии `.bak` на первом запуске новой версии с корректными прежними настройками startup helper создаёт его из проверенных байтов через temp + move до первого изменения main. Ошибка создания обязательного backup блокирует включение записи и ведёт на понятный экран ошибки. Нет существующих файлов — обычный первый запуск: первая успешная запись создаёт main; bak появляется при следующем изменении main либо на следующем проверенном старте. Дополнительная операция после commit не должна превращать успешное сохранение в сообщённый отказ.

**Восстановление до runtime.** Проверка читаемости использует parser provider, а не только «не нулевая длина». Ошибки доступа/I/O не считаются испорченным JSON и не запускают откат старой копии.

- Main корректен: обычный запуск. Восстановление не обходит существующую проверку каталога/журнала пространств.
- Main пустой/обрезанный/не принимается parser, bak корректен: сначала побайтово сохранить main в уникальный `Settings.json.corrupt-<UTC>-<guid>` через CreateNew; затем восстановить main через temp + flush + replace **без перезаписи `.bak` повреждёнными данными**. При любой ошибке сохранения оригинала/восстановления — blocked.
- Main отсутствует, bak корректен: восстановить через temp + move. Если оба отсутствуют — обычный первый запуск. Если main отсутствует, но имеется непригодный bak — blocked, а не первый запуск.
- Main повреждён, пригодного bak нет: сохранить main на месте, открыть recovery-экран; ничего не сбрасывать и не подключать task space.
- `.tmp` не принимаются за успешные сохранения и не восстанавливаются автоматически. Повреждённый bak также не удаляется.
- После восстановления дать заметное одноразовое сообщение: «Настройки восстановлены из резервной копии. Последние изменения настроек могли не сохраниться». Если далее выявляется проблема каталога, работает существующий recovery flow; tasks/Git jobs не запускаются в обход него.

**UI.** Локализованные RU/EN строки. Отдельный небольшой view появляется до `GetMainWindowViewModel`, инициализации storage, scheduler и update timer. Повторное открытие приложения после ручного восстановления проходит обычным путём. `Открыть папку` только открывает каталог файла; ошибка shell launch показывается здесь же.

Visual planning artifact — текстовый wireframe внутри SPEC; достаточен для двух кнопок и отсутствующего ранее раннего error flow:

```text
┌────────────────────────────────────────────────────────┐
│ Не удалось загрузить настройки                        │
│ Файл настроек повреждён. Подходящей резервной копии нет.│
│ Восстановите файл из своей копии и запустите снова.      │
│ [полный путь, перенос строк, возможность копирования]   │
│                                                        │
│ [Открыть папку]                           [Закрыть]      │
└────────────────────────────────────────────────────────┘
```

Для access denied/ошибки backup/restore — соответствующая причина без содержимого JSON. Стабильные ID: `SettingsRecoveryView`, `SettingsRecoveryMessage`, `SettingsRecoveryPath`, `SettingsRecoveryOpenFolder`, `SettingsRecoveryClose`. Клавиатура и узкое окно не обрезают текст; главные controls/task hotkeys недоступны.

Производительность: нет новых фоновых таймеров/очередей. Пропуск no-op уменьшает число flush. Замерить startup и массовые существующие `.Set` на синтетическом конфиге до/после; UI не должен подвисать на этом сценарии. При существенной регрессии — устранять избыточные записи в затронутом пути, а не молча ослаблять flush или добавлять широкую async-архитектуру.

### 6.3 User-Observable Scenarios

| Сценарий | Триггер | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| Обычная работа | Изменить тему/фильтр и перезапустить | Изменение сохранено, прежнее пространство доступно | restart integration + UI | 1, 2 |
| Прерывание записи | Завершить тестовый writer в контролируемой точке | При старте старые или новые целые настройки | subprocess tests | 3 |
| Повреждение с backup | Запуск с 0-байтным или обрезанным main | Восстановление, предупреждение, прежние задачи | Headless + FlaUI | 4 |
| Повреждение без backup | Такой же запуск без пригодной копии | Recovery-экран, путь, две кнопки; без сброса | Headless + FlaUI, hash файлов | 5 |
| Ошибка диска/прав | Невозможно записать/восстановить | Ошибка без разрушения пригодных копий | fault injection / OS check | 6 |

### 6.4 State / Interaction Matrix

| Состояние | Триггер | Результат |
| --- | --- | --- |
| Main валиден | Set / no-op | Опубликовать новый snapshot и bak / оставить файлы без изменений |
| Main валиден | Параллельные Set в процессе | Последовательный read–modify–write, оба независимых изменения остаются |
| Main повреждён | Есть корректный bak | Quarantine → atomic restore → ordinary runtime и предупреждение |
| Main повреждён | Bak нет/он повреждён | Заблокированный recovery view, дисковые файлы неизменны |
| Main отсутствует | Bak отсутствует / пригоден / повреждён | First run / restore / recovery view |
| Любой файл | Access denied или I/O error | Ошибка; не маскировать отсутствующим/пустым файлом |

### 6.5 Decision Ledger

| Решение | Owner | Выбор | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Место фикса | agent, утверждается SPEC | Исправить существующий provider в библиотеке; opt-in в App | 0.95 | Второй checkout и публикация пакета отдельным шагом | Нет, входит в данную SPEC |
| Сохранение API | agent | Существующие Set/Get и формат | 0.95 | Нужны тесты обоих overload | Нет |
| Recovery без backup | agent | Ошибка + папка + закрыть, без сброса | 0.95 | Требуется ручное восстановление настроек | Нет |
| Объём защиты | agent | Один bak, in-process serialization, атомарный main | 0.95 | Нет защиты от lost update между процессами/полного отказа диска | Нет |
| Платформы первой версии | agent, утверждается SPEC | Только Windows desktop; вне Windows прежний путь | 0.95 | Другие платформы пока не получают защиту | Нет, граница явно предлагается в SPEC |
| Публикация/установка | user | Не входят в EXEC автоматически | 1.0 | Локальная проверка не обновляет установленное приложение | Не для локального EXEC |

### 6.6 Runtime / Config / Data Contract Matrix

| Область | Source of truth | Изменение | Совместимость | Проверка |
| --- | --- | --- | --- | --- |
| Пользовательские настройки | Существующий Settings.json | Безопасная физическая запись | Тот же путь и JSON; без schema migration | Старый fixture → new save → старый reader |
| Бэкап | Settings.json.bak | Один прошлый читаемый snapshot | Добавочный файл рядом | Побайтовое сравнение и parse |
| Путь задач CLI | TaskStorage.Path/IsServerMode | Не меняется | Явный `--tasks` сохраняет приоритет | TaskDirectoryResolverTests |
| Server/bot | Текущий provider без opt-in | Без нового режима | Старый API и defaults | Build + full suite |
| Windows desktop | Общий App с явным Windows gate | Atomic writes + early recovery | Старые пути и формат; gate до любых новых файловых операций | Startup/UI и restart smoke |
| Другие UI-платформы | Прежний factory/entry points | Не меняются | Opt-in выключен, нет новых требований к ФС | Regression проверки default=false/platform gate; cross-target build без заявления о native runtime-проверке |

## 7. Бизнес-правила / инварианты

- Никогда не truncate основной файл в безопасном режиме.
- «Сохранено» означает публикацию полного файла; при неуспехе не публиковать новый in-memory snapshot как успешный.
- JSON-валидность не заменяет проверку каталога задач; существующие guard/journal остаются.
- Сбой save/recovery не стирает последнюю доступную пригодную копию.
- Копии содержат те же секреты, что исходник: создаются в том же пользовательском каталоге с не менее строгими правами; не попадают в Git, telemetry или UI-тестовые артефакты. В тестах только искусственные данные.

## 8. Точки интеграции и триггеры

- Оба `WritableJsonConfigurationProvider.Set` — единый безопасный путь при UseAtomicWrites=true; source factory прокидывает параметр.
- `App.InitializeRuntime` — Windows gate, recovery перед построением root, затем новый режим provider; вне Windows прежний путь.
- `App.OnFrameworkInitializationCompleted` — отдельная error view при blocked; восстановленный config идёт обычным маршрутом с предупреждением.
- `SettingsViewModel`, `MainWindowViewModel`, `TaskSourceSettingsAdapter`, `ServerStorage` сохраняют API вызовов. Отдельный новый updater hook не добавляется.

## 9. Изменения модели данных / состояния

Новых бизнес-полей и версии JSON нет. Добавляются `.bak`, уникальная forensic-копия только при recovery и собственный временный файл на save. Кратковременный результат recovery и сообщение живут в App; нормальная конфигурация остаётся единственным источником настроек.

## 10. Миграция / Rollout / Rollback

- EXEC реализует и проверяет оба локальных checkout. Пакет для проверки получает уникальную local/prerelease-версию в существующем local feed. Не перезаписывать cached 8.0.1.
- До публичной доставки требуется отдельно разрешённая публикация исправленного пакета; затем pin его доступной версии в `Directory.Packages.props`. Локальный unpublished package нельзя выдавать за воспроизводимую чистую CI-сборку или готовую установку.
- Первый запуск с корректным main создаёт первоначальный backup без изменения main. Ранее испорченный файл без backup не может быть восстановлен этим исправлением без дополнительного источника данных.
- Откат к прежнему binary не требует конвертации JSON. `.bak`/`.corrupt-*` сохраняются. Автоматический откат binary не подменяет main старым backup.
- Личная установленная версия и настройки не изменяются этой работой; end-to-end smoke использует опубликованный локально Release-exe с явным `--config` на отдельной fixture.

## 11. Тестирование и критерии приёмки

AC:

1. Старые scalar/object `.Set`, unknown keys, nested data и CLI resolution совместимы; после restart читается сохранённый результат. При cwd, отличном от AppContext.BaseDirectory, относительный `--config` разрешается как раньше, helper и provider работают с одним файлом, одноимённый cwd-файл не меняется.
2. In-process параллельные изменения из двух provider-инстансов одного пути не теряют независимые поля; no-op не вращает backup. Memory обновляется после подтверждённой записи.
3. Controlled subprocess termination до публикации и после неё оставляет parseable old/new main; старый writer воспроизводит повреждение в regression fixture до fix. Fault injection отдельно покрывает write/flush/replace error, не выдаётся за реальную проверку отключения питания.
4. Zero-byte/truncated main + валидный bak → восстановлен прежний task path, оригинал сохранён, bak не отравлен; пользователь видит сообщение. Повторный старт работает без нового восстановления.
5. Нет валидного backup, отсутствующий main с повреждённым bak, неверный корневой JSON → recovery view; ни одного task-storage, Git или update job; исходные файлы неизменны. Нормальный first run остаётся нормальным.
6. Access denied, ошибка сохранения corrupt-копии, backup failure и sharing violation не вызывают сброс/небезопасный overwrite. Startup-ошибки показаны в recovery view; runtime save возвращает диагностируемое исключение через существующий механизм вызывающего потребителя. Новая общая система обработки UI-ошибок сохранения не входит в scope; содержимое настроек в исключения не включается. На Windows копии не расширяют доступ при restrictive main и более открытом parent; при отказе установки прав конфиденциальные байты ещё не записаны.
7. Полный основной TUnit suite, Headless suite, релевантные FlaUI, сборка desktop, тесты библиотеки и compatibility smoke пройдены. Проверены default=false и platform gate: вне Windows новый flow не вызывается, прежние consumers компилируются; runtime-поддержка нового режима для них не заявляется. Объективный blocker отмечается как incomplete, а не PASS.

Уточнение AC3, явно принятое пользователем 2026-09-21 («Принимаю»): вместо требования выполнить контроль старого writer до реализации принят фактически выполненный поздний контроль неизменённого legacy writer. Функциональные требования AC3 не изменены; хронология evidence сохраняется и не называется pre-fix RED.

### Acceptance-to-Test Matrix

| AC | Автоматическая проверка | Visual/log check | Evidence |
| --- | --- | --- | --- |
| 1 | Библиотечные characterization + existing settings/task-space/CLI tests; relative config при cwd != BaseDirectory | Restart синтетического desktop config, проверка untouched cwd-файла | TRX, сравнение fixture |
| 2 | Parallel Set, two providers, same value, write failure | In-memory vs disk snapshot | Отчёт тестов библиотеки |
| 3 | Child writer + deterministic synchronization points; injected I/O errors | Main/bak bytes после выхода процесса | Exit codes, hashes, parse results |
| 4 | SettingsFileRecoveryTests + Headless + FlaUI | Warning, прежняя синтетическая задача, повторный запуск | after video/screenshots, TRX |
| 5 | Startup negative cases + Windows desktop recovery UI | Путь, 2 кнопки, отсутствие обычного main flow | Screenshot/video, file hashes |
| 6 | OS lock/readonly + injected write errors; restrictive ACL main/wider parent; отказ установки прав | Без секретов; Open folder failure видим | Test reports, synthetic error log, effective ACL всех оставшихся копий |
| 7 | Full main/headless + relevant FlaUI + build + library suite; default=false и Windows gate regression | Все обязательные результаты green; неподдержанные native runtime не объявлены проверенными | artifacts/test-results и local-only UI evidence |

На SPEC тесты не выполняются: здесь план, не заявление о PASS реализации. Команды EXEC после SDK/restore preflight (TUnit/MTP, не VSTest filter):

```powershell
dotnet --info
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/SettingsFileRecoveryTests/*" --maximum-parallel-tests 1
dotnet run --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -- --treenode-filter "/*/*/SettingsFileRecoveryFlaUiTests/*" --maximum-parallel-tests 1
```

Полный main/headless: существующий `scripts/ci/Invoke-TestStage.ps1` последовательно с `-Stage restore`, `build`, `test`, для `-Project main` и `headless`, свежий `-ResultsRoot artifacts/test-results/settings-crash-safety-<run>`. Library suite — по его фактическому runner после чтения project; не переносить TUnit flags наугад. `git diff --check` для обоих checkout.

UI evidence: `artifacts/settings-crash-safety/ui/`, local-only, искусственные данные. Before: старый Release-exe с нулевым fixture-config падает до окна — window-only video технически не может показать несуществующее окно; fallback: test launch/exit record и stderr/exception. After: запись автоматизированного FlaUI run для восстановленного main и error view; если recorder недоступен, назвать точную техническую причину и приложить screenshot + TRX + file comparison. Артефакты открыть и визуально проверить; не коммитить видео по умолчанию.

После обязательных green-проверок не расширять suite без новой находки. При timeout сначала проверить progress, runner и причину, затем менять проверяемую гипотезу. Не закрывать чужие процессы приложения и не запускать тесты на личных настройках.

## 12. Риски и edge cases

- Библиотека используется вне Unlimotion: opt-in режим ограничивает изменение поведения; новый API additive, прежний режим покрыт тестами.
- Защита этой версии ограничена Windows desktop. Android/Browser precreation и особенности Unix permissions не исправляются скрыто; другие consumers остаются на прежнем режиме.
- NTFS/local storage — основной проверяемый сценарий. Unsupported replace/сетевая ФС ведут к явной ошибке; небезопасного fallback нет. Устойчивость к отключению питания/дефекту диска не гарантируется одной rename/flush-последовательностью.
- Backup может быть синтаксически корректным промежуточным снимком task-space journal: проверяются оба вида journal recovery и непригодный каталог; startup не включает tasks до штатной проверки.
- Независимый внешний процесс может переписать настройки; это отдельный класс lost-update. Не называть текущую in-process блокировку межпроцессной гарантией.
- `Flush(true)` может замедлить частые настройки; no-op и замер обычного сценария нужны до принятия реализации.

### Expected User Review Objections

| Возражение | Почему вероятно | Как учтено | Статус |
| --- | --- | --- | --- |
| «Опять большая переделка» | Ранее предложены 14 мер | 3 слоя: writer, один bak, простой recovery; существующий API сохраняется | mitigated |
| «Сбросятся пути и задачи?» | Main был пустым | Нет silent defaults и подключения к новому хранилищу при corruption | mitigated |
| «Почему второй репозиторий?» | Ожидался локальный фикс | Private/nonvirtual writer не расширяем безопасно; маленький opt-in fix вместо копии API в приложении | mitigated |
| «После этого установленное приложение уже работает?» | Исходный инцидент реальный | Фикс проверяется отдельно; существующий пустой файл без backup требует отдельного восстановления, установка не выполнена | accepted-risk |

Rework Prevention Checklist: исходный сценарий сохранён; каждый AC связан с проверкой; решения и ограничения названы; UI wireframe и before/after evidence заданы; обязательный полный suite выбран из-за общего config-контракта; точный первоначальный writer не объявлен доказанным виновником.

## 13. План выполнения

1. Preflight обоих checkout, baseline failing/characterization fixtures.
2. Исправленный opt-in provider + библиотечные проверки; локальный уникальный nupkg.
3. Startup recovery, маленький UI, включение нового режима в App; тесты на synthetic config.
4. Полный обязательный набор, Release restart smoke, visual inspection, post-EXEC review.
5. Отдельный delivery gate: публикация пакета/Unlimotion и восстановление личного файла только при соответствующем поручении.

## 14. Открытые вопросы

Блокирующих продуктовых вопросов нет. SPEC утверждена; фактическая версия публикуемого пакета определяется при отдельном delivery preflight, а не угадывается здесь.

## 15. Соответствие профилю

Desktop/UI: новый error flow имеет wireframe, план UI tests, стабильные ID и visual evidence; длительные recovery-операции не блокируют уже открытое рабочее окно. Config/testing: запланированы old/new file, both Set overloads, compatibility, восстановление и полноценный suite. SPEC подтверждена; live config не меняется.

## 16. Таблица изменений файлов

| Файл / группа | Планируемое изменение | Причина |
| --- | --- | --- |
| WritableJsonConfiguration repo: provider/source, внутренний atomic-file helper при необходимости | Opt-in, lock, staged Data, temp/flush/replace/bak | Единственное место физической записи |
| WritableJsonConfiguration repo: Tests, package project metadata при локальной сборке | Regression/compatibility, уникальный local package | Безопасная проверка зависимости |
| `src/Directory.Packages.props` | Pin проверенной версии зависимости | Подключить исправление |
| `src/Unlimotion/Services/SettingsFileRecovery.cs` (новый) | Startup check/restore/result | Восстановление перед runtime |
| `src/Unlimotion/App.axaml.cs` | Atomic source, early error shell, warning | Главная интеграция |
| `src/Unlimotion/Views/SettingsRecoveryView.axaml[.cs]` (новые) | Простой recovery view | Понятная ошибка без VM/runtime |
| `src/Unlimotion.ViewModel/Resources/Strings[.ru].resx` | RU/EN сообщения | Локализация |
| `src/Unlimotion.Test/SettingsFileRecoveryTests.cs`, related existing contracts | Recovery + integration/compatibility | AC 1–6 |
| `tests/Unlimotion.AppAutomation.TestHost/*`, Headless/FlaUI recovery tests | Fixture routing + UI cases | Реальный startup и visual evidence |

Обновление changelog/пакетной документации — только фактическое поведение после EXEC, если требуется правилами репозитория; без заявлений о публикации. Другие изменения не входят в scope.

## 17. Таблица соответствий (было → стало)

| Область | Было | Станет |
| --- | --- | --- |
| Writer | Truncate единственного main | Полный temp → replace и предыдущая копия |
| Параллельные Set в процессе | Read–modify–write независимо | Критическая секция на путь |
| Corrupt startup | Необработанное исключение до окна | Проверенный backup либо понятный early error view |
| API/формат | IConfiguration и Set, JSON | Сохранены |

## 18. Альтернативы и компромиссы

- Новый локальный `ISettingsStore`/очередь: отвергнуты как слишком широкий scope.
- Подкласс существующего provider: не перехватывает nonvirtual object overload; отвергнут после чтения исходников.
- Локальная копия всей библиотеки/собственных extension methods: усложняет зависимости тестов/server/bot; выбран фикс источника пакета.
- Только backup или только catch: не закрывают сам механизм обнуления. Только atomic save: не помогает уже повреждённому main.
- Один bak дешевле истории версий, но может терять последние настройки. UI честно сообщает это при восстановлении.

## 19. Результат quality gate и review

### SPEC linter

PASS ниже означает достаточность проекта решения и плана проверки на фазе SPEC, а не успех ещё не выполненных тестов.

| № / блок | Статус | Проверяемое основание |
| --- | --- | --- |
| 1 / A | PASS | §1 и §6.3 сохраняют исходный симптом и наблюдаемый результат |
| 2 / A | PASS | §2: App/entry points/очередь и исходники provider 8.0.1; инцидент отмечен историческим |
| 3 / A | PASS | §3: truncate единственного файла и отсутствие раннего recovery; виновник конкретного обнуления не выдуман |
| 4 / A | PASS | §4: прежний API/JSON, одно место записи, одна копия |
| 5 / A | PASS | §5: нет новой архитектуры; первая версия Windows; установка/личный файл вне scope |
| 6 / B | PASS | §6.1: provider, helper, App и отдельный простой view |
| 7 / B | PASS | §8 и §16 связывают оба overload, Windows gate, startup и UI |
| 8 / B | PASS | §6.2 и §7: lock, staged Data, temp/flush/replace, first-run lifecycle |
| 9 / B | PASS | §6.4: main/bak missing/corrupt, доступ и I/O; нет silent defaults |
| 10 / B | PASS | §6.2: no-op и сравнительный замер; §12: flush — признанный риск |
| 11 / C | PASS | §9: нет новых бизнес-полей; описаны backup/temp/quarantine |
| 12 / C | PASS | §6.6 и §10: opt-in, прежние consumers/пути/формат, локальный пакет |
| 13 / C | PASS | §10: старый binary читает JSON, копии не удаляются, binary rollback не подменяет main |
| 14 / D | PASS | §11: AC1–7 включают содержимое файлов, память и startup side effects |
| 15 / D | PASS | §11: каждому AC соответствует проверка; есть fault/process termination/negative UI |
| 16 / D | PASS | §11 и §1: команды/runner, synthetic config, stop после нужных проверок, approval до EXEC |
| 17 / E | PASS | §13 и §16: библиотека → local nupkg → App → validation; доставка отдельна |
| 18 / E | PASS | §6.5 и §14: существенные решения приняты в предлагаемом scope, блокирующих вопросов нет |
| 19 / E | PASS | §0: expanded обязателен из-за config/storage, объём реализации ограничен |
| 20 / F | PASS | §6/§11/§15: desktop wireframe, UI tests, before/after evidence и testing-dotnet |

### SPEC rubric

| Критерий | Балл | Основание |
| --- | ---: | --- |
| Цель / границы | 5 | Один исходный сбой, три меры, явные Non-Goals и Windows scope |
| AS-IS | 5 | Проверены физический writer, overload routing, startup и consumers |
| Конкретность дизайна | 5 | Описаны commit, memory publication, backup и recovery branches |
| Безопасность / миграция / rollback | 5 | Нет сброса данных, копия оригинала, права, прежний формат и отдельная доставка |
| Проверяемость | 5 | AC→test/evidence, subprocess/fault tests, UI и synthetic restart |
| Автономность решений | 5 | Место фикса и границы определены; версия пакета выбирается по фактам EXEC |

Итого: 30/30 для готовности SPEC. Оценка не заменяет approval и не доказывает работоспособность реализации.

### Post-SPEC review-loop

- Статус: PASS для фазы SPEC; можно запрашивать подтверждение. Это не PASS реализации.
- Scope reviewed: этот SPEC, instruction stack из §0, expanded template, desktop/testing/UI profiles, план файлов §16, границы Windows и двух checkout. Открытых продуктовых вопросов нет.
- Scope/Evidence pass: прочитаны `App.axaml.cs`, Desktop `Program.cs`, `UnlimotionClientOptions.cs`, `SettingsViewModel`, `TaskSpaceSettingsPersistenceQueue`, `TaskSourceSettingsAdapter`, существующий MainControl recovery и Headless/FlaUI patterns, Android `MainActivity.cs`, Browser `Program.cs`, CLI resolver; upstream provider/source/factory/extensions и nuspec 8.0.1; `global.json`, test projects, `Invoke-TestStage.ps1`, `nuget.config`. Проверены git baseline/status и Microsoft File.Replace/Flush contracts. Личный Settings.json повторно не читался и не изменялся.
- Contract pass: сопоставлены §1/§5 с AC1–7, planned files, API compatibility, UI evidence, полным обязательным suite и раздельными delivery permissions. Фикс библиотеки добавочен и opt-in; поведение прочих платформ не переобещано.
- Adversarial risk pass: разобраны прерывание до/после публикации, ошибка после первого commit, два provider, неверный relative path, restrictive ACL/open parent, повреждение backup, missing-main bootstrap, синтаксически валидный journal snapshot и отсутствие usable backup. Это анализ контрактов на SPEC, не выполненные эксперименты.
- Отдельный reviewer `/root/settings_spec_review` использован по требованию review-loops для config. Фактический child sandbox — unrestricted/danger-full-access, approval never; техническая read-only изоляция недоступна. Агент сообщил, что не выполнял мутаций. Этот проход не заявляется технически read-only или независимой изолированной проверкой.
- Adversarial fallback основного агента: отдельно повторно прочитаны state/commit/recovery/permissions/platform contracts; контрпример «main уже опубликован, bootstrap backup не создался» закрыт исключением post-commit backup; контрпример «restore копирует restrictive bak в более доступный main» закрыт правами источника данных и отказом при несовместимых ограничениях. Остаточный риск writable/self-review указан, он не подменяет обязательные проверки EXEC.
- Role-Based pass: результаты ниже.
- Fix and re-review: перепроверены first-run backup, runtime-error promise, один resolved path, ACL источника/копий и все обещания других платформ по §0/§5/§6/§8/§11/§12/§16. После фиксов проверяются структура SPEC, отсутствие template placeholders и whitespace; изменён только этот файл.
- Stop decision: PASS. Отдельный reviewer после узкого re-review Windows gate/source сообщил отсутствие открытых находок; основной агент завершил adversarial fallback и проверку документа. Все 21 номерных раздела присутствуют, code fences парные, `git diff --no-index --check -- NUL <spec>` не выявил whitespace-ошибок (только предупреждение LF→CRLF). Git status содержит только новый SPEC. Дальше exact approval, а не реализация по умолчанию. Post-EXEC не выполнен, код/сборки/тесты не запускались.

### Role-Based Review Result

| Роль | Проверенный вопрос | Результат |
| --- | --- | --- |
| Business analyst / domain workflow | Не теряем ли прежнее пространство после corruption? | Нет silent defaults; restore использует прежний path; journal/catalog guard остаётся |
| UX / designer | Что увидит человек, если JSON не читается до MainVM? | Отдельный early view, конкретная причина/путь/2 кнопки, RU/EN; wireframe в §6 |
| Tester / validation | Докажет ли happy-path test устойчивость к прерыванию? | Нет; отдельно запланированы old-writer regression, child kill, faults, negative startup и UI |
| Developer / architect | Можно ли обойтись подклассом или очередью без библиотеки? | Нет для nonvirtual object Set; выбран малый source fix без новой storage-архитектуры |
| Delivery / operations / security | Не публикуем ли непроверенный пакет и не раскрываем ли копиями секреты? | Уникальный local package, отдельный publish gate, synthetic-only evidence, права до записи байтов |

### Findings и disposition

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Commit contract | Ошибка создания bak после первого main commit сообщала бы отказ уже успешной записи | Bootstrap bak до изменений существующего main; для first-run — при следующем save/start | fixed; §6.2 перепроверен |
| MEDIUM | Path compatibility | Helper мог проверять cwd-файл, а provider — AppContext.BaseDirectory | Один source PhysicalPath и relative-config regression | fixed; reviewer перепроверил §6.2/AC1 |
| MEDIUM | Secrets / file rights | Same-directory temp мог унаследовать более широкие ACL | Права источника до байтов, без расширения существующих ACL, negative test | fixed; reviewer проверил базовый контракт, fallback уточнил restore из bak |
| MEDIUM | Consumer / platform scope | Android/Browser создают `{}` раньше recovery и обходят missing-main branch | В первой версии включать новый flow только Windows; убрать single-view promise | fixed; узкий re-review reviewer: PASS |
| MEDIUM | Scope / runtime errors | Общее обещание UI-ошибок подразумевало незаказанный overhaul callers | Runtime exception по существующему механизму; новый экран только для startup | fixed; AC6 и §5 согласованы |

Depth checklist:

- Scope drift / unrelated changes: только SPEC; second repo явно предложен; Windows ограничивает, а не расширяет работу.
- Acceptance criteria: все семь связаны с evidence; их исполнение остаётся EXEC, не помечено green сейчас.
- User scenarios / ledger / objections: заполнены; отдельно объяснено, почему пустой личный файл без backup не восстановится автоматически.
- Validation evidence: inspected source и статические проверки SPEC; тестовые команды — план. Native runtime других платформ не заявлен проверенным.
- Unsupported claims: неизвестен точный первоначальный writer; rename/flush не названы универсальной защитой от потери питания; remote main не объявлен актуальным.
- Regression / edge cases: old API, оба overload, relative paths, no-op, memory/disk, first run, corrupt/missing backup, ACL и отсутствие task/update jobs.
- Comments/docs/changelog: изменён только проект решения; changelog после реализации описывает факты, не обещанную доставку.
- Hidden contract change: Windows opt-in и новый blocked startup явные; JSON, API и не-Windows defaults сохраняются.
- Manual-review challenge: проверено, что хорошая bak не заменяется corrupt main, а восстановление не создаёт новое пустое пространство и не активирует обычные jobs до проверки каталога.

No-findings justification: исходный проход нашёл перечисленные нарушения и исправил их на уровне SPEC. После Fix and re-review открытых находок нет: Windows gate/source/ACL/first-run/recovery согласованы с AC и планом файлов. Multi-process lost update, аппаратный отказ, ограничения одного backup и отсутствие защиты остальных платформ — явные границы, а не скрытые обещания.

### Post-EXEC

- Baseline RED: новый FlaUI test падает до окна на старом App с пустым синтетическим main и пригодным bak; TRX/HTML `artifacts/settings-crash-safety/ui/baseline-red/`. Отдельно обычный pre-integration Release publish запущен с 0-byte synthetic config: exit `-532462766`, `InvalidDataException → FormatException → JsonReaderException`; `artifacts/settings-crash-safety/baseline-release-stderr.log`. Before video неприменим: окно не возникает.
- Recovery unit first pass: 23/23 green, `artifacts/settings-crash-safety/recovery-unit/`; corruption, missing backup, I/O, ACL, bootstrap и повторы. Это ещё не full validation.
- Библиотечный baseline RED: Set менял память до failed save (2/4 failed, 2 characterization passed). C# binding в обоих baseline случаях выбирал object overload; независимый pre-fix RED string overload не заявляется. Финальный suite явно проверяет обе перегрузки.
- Подключён локальный пакет `8.1.0-settings-safety.20260921.4`, SHA256 `080985C3786AD9890767C6AF04117CFD71555A80DE47F45764B1A56AB3620697`; `netstandard2.0` и default=false сохранены. Library Release: 38 passed, 1 non-Windows-only test skipped (`../WritableJsonConfiguration/artifacts/test-results/acl-final/full.trx`). Пакет не опубликован, clean CI без local feed не заявляется воспроизводимым.
- Follow-up публикации по отдельному поручению пользователя: PR [WritableJsonConfiguration #29](https://github.com/Kibnet/WritableJsonConfiguration/pull/29) выпустил `8.1.0`. Его post-merge review обнаружил два edge case atomic path navigation: числовой ключ объекта не обновлялся, sparse array index мог записаться в другой индекс. Оба воспроизведены новым RED 0/4; исправлены в PR [#30](https://github.com/Kibnet/WritableJsonConfiguration/pull/30), targeted5/5 и полный Windows suite42/42 green +1 platform skip, Ubuntu/.NET8 CI green. Публичный `8.1.1` индексирован NuGet.org, clean public restore/build/atomic-write smoke пройден; public nupkg SHA256 `65868AD9435433538C86F21CC497CF65B90D0563B132AEAC6C79420904745A50`, repository commit `3d20cf8c344c427cf8a7f27722e40077f56d4bf1`, GitHub Release `v8.1.1` Latest. Release `v8.1.0` помечен как superseded. Историческое evidence local `.4` выше не переписывается, но для доставки использовать только `8.1.1` или новее.
- Follow-up PR Unlimotion: первый CI с публичным `8.1.1` воспроизвёл ложную несовместимость ACL на GitHub-hosted Windows после `File.Replace`: main содержал одинаковые effective allow-ACE как explicit и inherited, backup — один inherited ACE. Библиотечный PR [#31](https://github.com/Kibnet/WritableJsonConfiguration/pull/31) ограниченно нормализует file-irrelevant propagation flags и дедуплицирует только побайтно одинаковые ACE после удаления provenance; SID, mask, `InheritOnly` и Allow/Deny ordering сохраняются. Ubuntu и новый Windows CI green; `8.1.2` опубликован. `src/Directory.Packages.props` переведён на публичный `8.1.2`; clean NuGet.org restore подтвердил SHA256 `E5A797238AF32FB95832650E1645236CA1C3C4BCFCD7358F5D26A4B7F46C559B`, а integration restore одновременно сохранил штатный local `NodifyAvalonia/6.6.0-unlimotion.a12.1`. Установленная версия приложения и реальные пользовательские настройки не изменялись; выпуск и установка остаются отдельными действиями.
- Library evidence/команды: `../WritableJsonConfiguration/artifacts/validation/library-evidence.md`. Четыре controlled kill checkpoints сохраняют old/new main и пригодный bak; реальные sharing violations и injected failures проверяют память/диск, no-op и обе перегрузки. Замер 100 сохранений JSON 22 KB: legacy143–310ms, atomic493–1111ms; no-op legacy201–282ms, atomic41–84ms. Это шумный локальный замер при фоновой нагрузке, не claim ускорения: durable flush увеличивает цену save.
- Отклонение порядка regression: legacy truncate/control был прерван после наблюдаемого начала `File.WriteAllText` уже после реализации нового режима. Legacy writer остался неизменным, поэтому это проверка механизма старой версии, но не pre-fix RED по времени. До фикса реально получены memory/disk RED и падение App до окна; эти результаты не подменяются более поздним экспериментом. Исходный виновник реального обнуления не установлен.

#### Fix and re-review в EXEC

| Severity | Area | Finding | Исправление / evidence | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Relative config | Присваивание resolved path в `_configPath` меняло cwd-путь существующего tree-state sidecar | Только локальный `physicalPath` для provider/recovery; runtime regression сохраняет старый sidecar и cwd homonym | fixed |
| MEDIUM | Nested settings | Legacy traversal искал вложенный child от root и терял siblings при глубине 3; staged Data проявил потерю сразу | Узкий atomic-only current-context traversal; legacy merge/tail неизменны; 4 RED → GREEN | fixed |
| LOW | Error guidance | Reload не снимает блокировку после unreconciled I/O | Сообщение и README требуют restart/recreate root | fixed |
| HIGH | Windows ACL | Raw SDDL equality отклоняла собственную bootstrap-копию после Windows normalization | Fingerprint игнорирует только inherited provenance обычных ACE, порядок меняется лишь внутри смежной Allow/Allow или Deny/Deny группы. SID/mask/прочие flags и Allow↔Deny границы сохранены. Bootstrap RED → helper24 GREEN, library38 GREEN | fixed |
| HIGH | Early startup | Валидный main с несовместимыми правами существующего bak доходил до первого Set и падал вне early shell | Проверка совместимости до Ready, без изменения файлов; helper + реальный InitializeRuntime: 2 RED → GREEN, добавлен native/Headless case | fixed |
| MEDIUM | UI test evidence | Border отсутствует в native UIA; окно при 225% DPI захватывалось с чужим desktop edge | Stable close anchor + exact visible localized text; неверные 13 PNG/MP4 удалены, логи сохранены; client-only variant skill recorder в ignored artifacts | fixed; native10/10 GREEN |
| MEDIUM | AC3 / порядок проверки | Контроль аварийного прерывания неизменённого legacy writer выполнен после реализации, хотя исходный AC3 требовал «до fix» | Пользователь 2026-09-21 ответил «Принимаю» на точный вопрос о замене pre-implementation прогона поздним контролем старого writer; уточнение AC3 записано в §11, фактическая хронология сохранена | closed — user-approved AC3 amendment |

- Проверенный ACL-контракт: [Microsoft — порядок ACE](https://learn.microsoft.com/en-us/windows/win32/secauthz/order-of-aces-in-a-dacl) и [почему нельзя менять местами Allow и Deny](https://devblogs.microsoft.com/oldnewthing/20070608-00/?p=26503). Это консервативное сравнение, не универсальный ACL merge; несовместимые ограничения по-прежнему блокируют операцию.
- Reviewer `/root/settings_spec_review` перепроверил relative path, nested traversal, окончательный ACL delta, UIA assertions и hash пакета: новых code findings нет. В финальном проходе самостоятельно сверил TRX main1111/scoped123/Headless51/native10, stage-test exit0, build tails, benchmark JSON и representative client-only кадры restored/invalid-root/access-denied; новых validation findings нет. До решения пользователя единственной открытой находкой был порядок проверки AC3, итог reviewer — ASK-HUMAN; последующее явное согласие пользователя закрывает именно эту находку, а не переписывает результат прежнего review. Фактический sandbox writable/unrestricted; этот проход не объявляется технически read-only. Основной агент дополнительно проверил null/empty DACL, неподдержанные ACE, порядок Allow/Deny, публикацию Data и границы first-run/restore.

#### Итоговое evidence EXEC

| Проверка | Фактический результат | Артефакт |
| --- | --- | --- |
| Full main, repo restore/build/test stages | 1111/1111, 20m29s; выполнен до последнего узкого startup ACL guard | `artifacts/test-results/settings-crash-safety-final-4/main/` |
| Последний guard, RED | 2 failures из123: helper ошибочно Ready и реальный runtime IOException | `artifacts/settings-crash-safety/startup-acl-red/` |
| Settings* после guard | 123/123, 21s; включает25 helper и7 реальных startup cases | `artifacts/settings-crash-safety/settings-final-diagnostic/` |
| Native FlaUI на final published Release | 10/10: 9 regression cases + opt-in before/after startup characterization; 2m22s | `artifacts/settings-crash-safety/flaui-final-5/` |
| Full Headless после последнего guard | 51/51, 1m54s; включает9 новых recovery UI cases | `artifacts/test-results/settings-crash-safety-final-4/headless/` |
| Library full Release | 38 passed, 1 non-Windows-only skipped | `../WritableJsonConfiguration/artifacts/test-results/acl-final/full.trx` |
| Стандартная Desktop Release build и local publish | 0 errors; оставшиеся предупреждения не выдаются за исправленные | `artifacts/settings-crash-safety/desktop-release-build-final.log`, `desktop-release-publish-final.log` |
| Browser / Android / TelegramBot Release | Все exit0, без диагностических исключений сборки; до последнего Windows-only ACL guard | `artifacts/settings-crash-safety/build-Unlimotion.*-4.log` |
| CLI/server compatibility | Проекты компилируются в main build, CLI resolver/integration и journal/fault tests входят в1111 | Main TRX и build log |
| PR validation: public 8.1.1 restore/build | main и Headless restore/build exit0; Desktop Release publish exit0 | `artifacts/settings-crash-safety/pr-public-8.1.1/`, `pr-public-8.1.1-release/` |
| PR validation: Settings* | 123/123, 25s | `artifacts/settings-crash-safety/pr-public-8.1.1-settings-2/` |
| PR validation: full Headless | 51/51, 2m15s | `artifacts/settings-crash-safety/pr-public-8.1.1-headless/` |
| PR validation: native FlaUI | 10/10, 2m45s; 8 новых client-only MP4 по 4s, 15FPS, 2222×1496 | `artifacts/settings-crash-safety/flaui-pr-public-8.1.1/`, `ui/pr-public-8.1.1/` |
| PR validation: full main | 1112/1113; один старый `TaskGraphWorkspaceCommandScenario_ExecutesFeatureSteps` получил межтестовый count +2, изолированный повтор 1/1 green | `artifacts/settings-crash-safety/pr-public-8.1.1/main/`, `pr-public-8.1.1-targeted-existing/` |
| Первый PR CI на 8.1.1 | 1107/1113 main: 5 recovery/ACL + 1 first-run false mismatch; Headless green. Finding воспроизведён в новом Windows CI библиотеки | Unlimotion action `35601992716`, WritableJsonConfiguration PR31 |
| Public 8.1.2 clean/integration restore | NuGet.org indexed; public nupkg SHA256 `E5A797238AF32FB95832650E1645236CA1C3C4BCFCD7358F5D26A4B7F46C559B`; package 8.1.2 + штатный local Nodify разрешены без fallback | `artifacts/settings-crash-safety/public-only-8.1.2/`, `public-integration-8.1.2/` |
| Public 8.1.2 Settings* | 123/123, 24s | `artifacts/settings-crash-safety/public-integration-8.1.2/settings/` |
| Второй PR CI на 8.1.2 | first-run/library writer исправлен; 5 restore cases всё ещё Blocked из-за отдельной копии старого comparator в App helper; Headless green | Unlimotion action `35605982781` |
| App helper ACL normalization | Синхронизированы те же узкие правила; прямой duplicate/provenance/propagation/`InheritOnly`/mask regression; Settings* 124/124 | `artifacts/settings-crash-safety/public-integration-8.1.2/settings-app-acl-fix/` |

Ни один retry не добавлен в тесты. Промежуточный `startup-acl-green` дал 122/123: relative-path case неожиданно вернул Blocked, первопричина этого единичного результата не установлена. После добавления безопасного диагностического сообщения, без изменения production behavior, изолированный runtime7/7 и тот же Settings*123/123 прошли. Не называть этот эпизод доказанно исправленным дефектом; исходный отчёт сохранён.

При повторной локальной проверке с публичным `8.1.1` полный main-набор дал 1112/1113: единственный сбой возник в существующем task-graph сценарии и не затронул settings/recovery; тот же тест без изменения кода прошёл изолированно 1/1. Первый чистый Windows CI затем обнаружил отдельный воспроизводимый ACL portability defect `8.1.1`, описанный выше. После выпуска `8.1.2` второй CI подтвердил исправление library writer, но выявил оставшуюся копию старого comparator в App recovery helper; она синхронизирована отдельным commit и покрыта прямым regression. Окончательный чистый Windows CI Unlimotion остаётся обязательным gate PR.

Замер запуска: один warmup каждого binary, затем две пары с чередованием порядка; независимый synthetic profile каждого запуска, измеряется launch → видимая исходная задача. Before5316/5488ms, after5069/4952ms; raw `artifacts/settings-crash-safety/ui/after-final/normal-startup-before-after.json`. Существенное замедление на этих двух парах не наблюдалось; статистическое ускорение и отсутствие любых UI pauses этим не доказаны.

Visual evidence: `artifacts/settings-crash-safety/ui/after-final/`, 8 client-only MP4 по4s,15FPS,2222×1496, без звука. Все непустые, ffprobe проверен. Просмотрены извлечённые midpoint PNG: восстановленная задача + не исчезающее самостоятельно предупреждение; RU error view для пустого/неверного/отсутствующего main и несовместимых прав. Полный путь, обе кнопки и текст видны; отсутствие task-controls дополнительно проверено UIA. Три restored MP4 имеют одинаковый SHA256, поскольку synthetic данные и видимое состояние одинаковы. Зелёное migration-уведомление — прежнее поведение fixture, не часть этой доработки. Before fallback — crash/exit/exception до появления окна, а не выдуманное видео. Skill `record-app-screen` применён с локальной client-rectangle адаптацией из-за DPI; `run-tunit-tests` определил корректный MTP workflow. Артефакты local-only, не включаются в Git.

Граница ошибок: ранний экран обрабатывает ошибки проверки/копирования/восстановления Settings и статическую несовместимость ACL. Он не является общим обработчиком ошибок последующей runtime-инициализации: поздний I/O при Set, ошибка сети/хранилища и другие причины за этой границей остаются в прежнем механизме. Данные не сбрасываются; UI-overhaul всех runtime errors по-прежнему вне scope.

#### Full post-EXEC review

- Scope reviewed: утверждённый SPEC, оба git status/diff, новые helper/view/tests, App integration, package/source API, README библиотеки, TRX/logs/hash и просмотренные UI кадры. Changelog/release notes не создаются: локальная реализация не опубликована, release ещё не выбран.
- Scope/Evidence pass: Windows-only guard до consumers, один physical path, прежний sidecar path, default=false библиотеки, no-op/serialized Set/Flush/Replace, восстановление без defaults и отсутствие storage/jobs проверены по коду и evidence выше.
- Contract pass: user-observable scenarios §6.3 и AC1–7 сопоставлены с тестами. Отдельно отмечены поздний scoped rerun после полного main и отклонение времени legacy-control; не утверждается полный main после последнего guard или pre-fix timing позднего control.
- Adversarial fallback основного агента: контрпримеры nested siblings, source-vs-cwd sidecar, ACL provenance/порядок deny, bootstrap save, несовместимый existing bak, ambiguous commit, first run и backup poisoning проверены. Последний статический ACL gap найден и исправлен в этом проходе. Поздние runtime I/O не спрятаны за обещанием универсального восстановления.
- Role-Based pass: domain — прежняя task projection и CLI override сохранены; UX — причина/путь/две кнопки RU/EN, dismissible persistent warning и кадры просмотрены; tester — RED/GREEN, full/targeted/native evidence раздельны; developer — нет нового store/queue/schema; operations/security — секреты не выводятся, synthetic-only, права до байтов, доставка не выполнена.
- Fix and re-review: все перечисленные code findings исправлены; область последнего guard перепроверена Settings*123, actual native10, full Headless51 и final Release build, library38 без дальнейших изменений. TRX totals проверены повторно чтением отчётов. Оба checkout и все новые untracked файлы прошли whitespace check; предупреждения только о LF→CRLF. Полный main после узкого guard не повторялся: scoped revalidation соответствует review-loops и не выдаётся за повтор1111 тестов.
- Depth checklist: unrelated changes не включены; AC/scenarios/objections сопоставлены; factual/performance/install claims ограничены реальными проверками; legacy API/defaults остаются; documentation указывает local-package и platform limitations. Manual-review challenge дал конкретные ACL/sidecar/nested-data findings, а не формальное «нет замечаний».
- Expected objections: новая инфраструктура не добавлена; автоматического сброса нет; второй checkout нужен для nonvirtual writer; установленная версия не заменена. Требуется отдельная публикация пакета, затем выпуск/установка и отдельное решение о личном повреждённом файле.
- Stop decision: PASS для локальной реализации после решения пользователя 2026-09-21. Code findings закрыты, обязательные проверки выполнены, единственное процедурное отклонение AC3 явно принято пользователем и отражено в §11. Это не заявление о соблюдении исходного порядка прогона. Публикация/установка не выполнялись и не разрешены этим результатом; ранее описанные residual risks сохраняются.
- Решение человека: на вопрос «Принимаем контрольное аварийное прерывание неизменённого старого writer, выполненное после реализации, вместо предусмотренного в AC3 прогона до реализации?» получено «Принимаю». Неотвеченных вопросов для локальной приёмки нет; новый scope и доставка этим не согласованы.
- Closing re-review: основной агент сверил ответ с единственной открытой находкой и обновил только эту SPEC. Код, пакет и тесты не менялись, повтор успешных runtime-проверок не требуется; выполнена статическая проверка документа.

## Approval

Получено 2026-09-21: «Спеку подтверждаю». Разрешена локальная реализация описанного scope в Unlimotion и исходниках WritableJsonConfiguration, но не публикация или замена личных настроек.

Дополнительное решение 2026-09-21: «Принимаю» — согласован поздний контроль неизменённого старого writer вместо предусмотренного AC3 прогона до реализации. Согласие относится только к этому отклонению порядка проверки; commit/push/PR, публикация пакета, выпуск, установка и изменение личных настроек не разрешались.

Отдельное поручение 2026-09-21: «Хорошо приступаем к публикации `WritableJsonConfiguration`». По нему разрешены и выполнены commit/push/PR, публикация NuGet/GitHub Packages и GitHub Releases библиотеки. Это поручение не разрешает выпуск/установку Unlimotion или изменение личных настроек.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток работы | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| 2026-09-21 SPEC: scope | Сократить широкий набор мер до writer/bak/recovery | Проверены App, SettingsVM, queue, existing UI tests | Review | «без оверинжиниринга, подготовь спеку» | Этот документ |
| SPEC: место исправления | Opt-in patch библиотеки; не подкласс provider | Прочитаны исходники 8.0.1, private Save/nonvirtual Set(object) | Проверить контрпримеры и integration scope | Не требуется до review | Этот документ |
| SPEC: review / упрощение | Исправлены commit/path/ACL контракты; первая версия только Windows desktop | Reviewer re-review PASS + adversarial fallback; без read-only sandbox; static checks пройдены | Ожидать «Спеку подтверждаю» | Windows scope предложен этой SPEC; approval ещё нет | §19 |
| 2026-09-21 EXEC: approval/preflight | Реализовать утверждённый scope; взять актуальный main без потери изменений | HEAD 53dacb06, SDK 10.0.401; библиотека aa75067 в отдельном checkout | Regression → implementation → validation | «Спеку подтверждаю» | Два локальных checkout, без публикации |
| EXEC: regression и implementation | Baseline воспроизведён, helper/UI/App integration добавлены; UI worker владеет только новыми tests/TestHost и SettingsStartupRuntimeTests | Debug FlaUI RED + Release process RED; recovery unit23/23 | Подключить финальный local nupkg, staged validation | Scope прежний | §19 / artifacts/settings-crash-safety |
| EXEC: Fix and re-review | Исправлены обнаруженные sidecar/nested/ACL/early-shell нарушения; контракты не скрыты за fallback | Library38+1skip, main1111 до последнего guard; после guard Settings123, Headless51, native10, Desktop Release build/publish | Завершить evidence review без повторов успешных проверок | Дополнительных разрешений не требовалось | §19, TRX и inspected client-only видео |
| EXEC: validation / stop до решения пользователя | Реализация локальная, доставка не выполнена. Сохранён единичный необъяснённый intermediate relative-path failure; повторные scoped проверки green | ASK-HUMAN только по буквальному «до fix» в AC3, фактический старый writer проверен после реализации | Получить узкое решение по процедурному отклонению; не объявлять общий PASS заранее | На тот момент ожидалось; последующее решение — следующая строка | §19 / full post-EXEC review |
| 2026-09-21 EXEC: приёмка | Пользователь явно принял единственное отклонение AC3; функциональный scope и evidence не меняются | PASS локальной реализации; в этом шаге изменена только SPEC, без повторного запуска тестов | Локальная работа завершена; доставка только по отдельному поручению | «Принимаю» | §11, §19, Approval |
| 2026-09-21 Follow-up: публикация библиотеки | Опубликовать стабильную minor-версию; после post-merge findings немедленно выпустить patch, не скрывать дефект `8.1.0` | PR29/CI/release `8.1.0`; RED0/4 → fix → targeted5/5, full42+1skip; PR30 review/CI green; public restore/smoke `8.1.1`; SHA256 и commit read-back | Перевести Unlimotion с local `.4` на публичный `8.1.1` отдельным шагом | «Хорошо приступаем к публикации `WritableJsonConfiguration`» | NuGet `8.1.1`, PR30, release `v8.1.1`, action `35597685394` |
| 2026-09-21 Follow-up: PR Unlimotion | Закрепить публичный `WritableJsonConfiguration`; первый CI выявил Windows ACL portability defect 8.1.1, поэтому выпущен узкий patch 8.1.2 с постоянным Windows CI | PR31 Ubuntu/Windows green; NuGet 8.1.2 public restore + Settings123; повторный Unlimotion CI обязателен | Не менять установленную версию и реальные пользовательские настройки | «Теперь оформи PR для unlimotion» | Unlimotion PR305, library PR31/release v8.1.2 |
