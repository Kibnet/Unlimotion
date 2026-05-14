# Переключение типа Git remote в настройках

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex / пользователь
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: менять код только после подтверждения спеки; сохранить существующий flow Git backup; UI-изменение покрыть AppAutomation/Avalonia Headless тестом; не выполнять сетевые операции к реальным remote
- Связанные ссылки: `AGENTS.override.md`; `C:\Users\Kibnet\.codex\agents\instructions\profiles\dotnet-desktop-client.md`; `C:\Users\Kibnet\.codex\agents\instructions\profiles\ui-automation-testing.md`

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
Добавить в окно настроек Git backup возможность переключать выбранный remote между HTTP и SSH типом подключения. Если для того же repository уже есть remote с адресом целевого типа, пользователь должен переключиться на него. Если такого remote нет, приложение должно создать копию выбранного remote с URL другого типа и выбрать ее.

Outcome contract:
- Success means:
  - В настройках виден явный UI-контрол для выбора типа подключения `HTTP` / `SSH` для выбранного remote.
  - При выборе другого типа приложение либо выбирает существующий remote с эквивалентным адресом, либо создает новый remote с преобразованным URL.
  - Дубликат remote с тем же целевым URL не создается.
  - Поле `GitRemoteUrl`, выбранный `GitRemoteName`, auth UI (`Token`/`SSH`) и список remotes обновляются согласованно.
  - Изменение покрыто ViewModel/service тестами и UI automation тестом.
- Итоговый артефакт / output: изменения в ViewModel, Git service, настройках UI, локализации и тестах.
- Stop rules:
  - Остановиться до EXEC, пока пользователь не напишет `Спеку подтверждаю`.
  - На EXEC останавливать retrieval, когда найдены все места чтения/создания remotes, binding-команды настроек и принятый UI test pattern.
  - Завершать validation только после targeted UI/test прогонов и full `dotnet test` либо явной фиксации блокера.

## 2. Текущее состояние (AS-IS)
- Экран настроек находится в `src/Unlimotion/Views/SettingsControl.axaml`.
- Git backup state живет в `src/Unlimotion.ViewModel/SettingsViewModel.cs`.
- `SettingsViewModel.ReloadGitMetadata()` читает `Remotes`, формирует `RemotesWithAuthType`, читает `Refs`, выбирает remote и синхронизирует `GitRemoteUrl`.
- `GitRemoteNameDisplay` сейчас работает через строку вида `name (HTTP/SSH)` и при выборе remote обновляет `GitRemoteName`, `GitRemoteUrl` и auth mode.
- `IRemoteBackupService` уже умеет читать `Remotes()`, `GetRemoteAuthType(remoteName)` и `GetRemoteUrl(remoteName)`.
- Реальная работа с repository metadata находится в `src/Unlimotion/Services/BackupViaGitService.cs`; remotes читаются через LibGit2Sharp `repo.Network.Remotes`.
- В UI есть поле "Remote source", но нет отдельного контрола смены HTTP/SSH и нет команды создать alternate remote.
- Существующий UI suite есть в `tests/Unlimotion.UiTests.Authoring`, `tests/Unlimotion.UiTests.Headless`, `tests/Unlimotion.UiTests.FlaUI`.

Ограничения и проблемы:
- URL конвертация между `https://github.com/owner/repo.git` и `git@github.com:owner/repo.git` пока не выделена в контракт.
- Нельзя создавать remote при каждом переключении вслепую: нужно сначала проверить существующие remote URLs.
- Для unsupported URL formats нужно не ломать текущие настройки и показать/зафиксировать понятное состояние.

## 3. Проблема
Пользователь не может прямо из настроек переключить выбранный Git remote между HTTP и SSH, поэтому вынужден вручную редактировать remote в Git или URL в настройках; при наличии только одного типа подключения приложение не помогает создать безопасную копию remote с другим URL.

## 4. Цели дизайна
- Разделение ответственности:
  - `SettingsViewModel` управляет выбранным типом подключения, availability и синхронизацией UI state.
  - `BackupViaGitService` инкапсулирует чтение/создание Git remotes и URL conversion.
  - `SettingsControl.axaml` только отображает переключатель и кнопку/команду.
- Повторное использование: использовать существующие `ReloadGitMetadata()`, `GetRemoteAuthType()`, `GetRemoteUrl()` и auth mode refresh.
- Тестируемость: выделить pure/helper logic для конвертации URL и проверки дублей; покрыть ViewModel сценарии fake service, service сценарии temp repository, UI сценарий AppAutomation.
- Консистентность: UI должен вести себя одинаково при одном и нескольких remotes.
- Обратная совместимость: существующие настройки `Git.RemoteUrl`, `Git.RemoteName`, `Git.PushRefSpec` сохраняются; существующие HTTP/SSH sync flows не меняются.

## 5. Non-Goals (чего НЕ делаем)
- Не добавлять поддержку произвольных Git hosting providers сверх форматов, которые можно надежно конвертировать из URL (`https://host/owner/repo.git`, `http://host/owner/repo.git`, `git@host:owner/repo.git`, `ssh://git@host/owner/repo.git`).
- Не удалять и не переименовывать существующие remotes.
- Не менять push/pull/clone алгоритмы, кроме выбора remote через уже существующие настройки.
- Не выполнять реальные сетевые обращения к GitHub/GitLab в тестах.
- Не менять формат task-файлов и persisted config schema.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.ViewModel/IRemoteBackupService.cs` -> добавить контракт переключения/создания alternate remote и, при необходимости, DTO результата.
- `src/Unlimotion/Services/BackupViaGitService.cs` -> реализовать поиск существующего equivalent remote, конвертацию URL и создание нового remote.
- `src/Unlimotion.ViewModel/SettingsViewModel.cs` -> добавить свойства/команду для выбранного connection type, статуса availability и вызова service; после успеха обновлять metadata.
- `src/Unlimotion/App.axaml.cs` -> подключить команду ViewModel к async service call, с busy/error state по существующему паттерну backup commands.
- `src/Unlimotion/Views/SettingsControl.axaml` -> добавить UI control около "Remote source" для `HTTP`/`SSH` и stable automation-id.
- `src/Unlimotion.ViewModel/Resources/Strings*.resx` -> добавить локализацию новых labels/status/errors.
- `tests/Unlimotion.Test/*` -> добавить unit/integration-ish tests.
- `tests/Unlimotion.UiTests.Authoring/*` -> добавить UI сценарий и page selectors.

### 6.2 Детальный дизайн
- Добавить модель результата, например `RemoteConnectionTypeSwitchResult(string RemoteName, string RemoteUrl, string AuthType, bool CreatedRemote)`.
- Добавить service method, например `SwitchRemoteConnectionType(string remoteName, BackupAuthMode targetMode)`.
- Алгоритм service:
  - найти выбранный remote по `remoteName`;
  - определить текущий URL;
  - построить целевой URL для `HTTP` или `SSH`;
  - если целевой URL равен текущему URL, вернуть текущий remote без изменений;
  - найти существующий remote с таким целевым URL через ordinal comparison after normalization rules;
  - если найден, вернуть найденный remote;
  - если не найден, создать новый remote с уникальным именем, производным от исходного (`<remoteName>-http` или `<remoteName>-ssh`, при конфликте добавить числовой суффикс);
  - вернуть созданный remote и URL.
- URL conversion:
  - SSH scp-like `git@host:owner/repo.git` -> HTTP `https://host/owner/repo.git`;
  - SSH URI `ssh://git@host/owner/repo.git` -> HTTP `https://host/owner/repo.git`;
  - HTTP/HTTPS `https://host/owner/repo.git` -> SSH `git@host:owner/repo.git`;
  - unsupported URL -> controlled error без изменения remotes.
- Canonical comparison для duplicate avoidance:
  - поддерживаемые URL приводятся к canonical identity `(connection-family, host, path)`;
  - `connection-family`: `http` для `http://` и `https://`, `ssh` для `git@host:path` и `ssh://git@host/path`;
  - `host` сравнивается case-insensitive и без default port (`:80` для HTTP, `:443` для HTTPS, `:22` для SSH);
  - `path` сравнивается case-sensitive после удаления ведущих `/`, одного финального `/` и одного финального `.git`;
  - query/fragment/userinfo кроме SSH user `git` не поддерживаются и дают controlled error;
  - exact URL string comparison допускается только как быстрый путь до canonical comparison.
- UI:
  - рядом с `RemoteSource` добавить компактный `ComboBox` или segmented-style `ComboBox` с `HTTP` и `SSH`.
  - Команда переключения выполняется только по явному пользовательскому действию. Initial binding, `ReloadGitMetadata()` и programmatic selection update должны проходить через suppression guard или отдельный setter path без service mutation.
  - После успеха выбрать remote из результата, обновить `GitRemoteUrl`, `GitRemoteNameDisplay`, `RemotesWithAuthType`, `BackupAuthMode`.
  - При ошибке оставить прежний выбор и показать status через `BackupStatusText` / error toast по существующему паттерну.
- output contract / evidence rules:
  - Unit tests должны доказывать URL conversion и duplicate avoidance.
  - UI test должен доказывать, что в settings flow можно переключить SSH/HTTP тип при одном исходном remote и увидеть SSH controls/token controls в зависимости от результата.
- границы сохранения поведения:
  - Существующий ручной ввод `GitRemoteUrl` продолжает определять auth mode, если repository remotes недоступны.
  - Существующий выбор remote из `Remote source` продолжает синхронизировать URL.
- обработка ошибок:
  - repository not initialized -> команда disabled или controlled status.
  - selected remote missing -> controlled status and metadata reload.
  - unsupported URL -> controlled status, без создания remote.
- производительность:
  - Операция локальная, без network; выполнять repository mutation в background task, UI обновлять после завершения.

## 7. Бизнес-правила / Алгоритмы
| Условие | Действие |
| --- | --- |
| Target type совпадает с текущим auth type выбранного remote | Не создавать remote, сохранить выбор |
| Target URL уже есть у другого remote | Выбрать существующий remote |
| Target URL отсутствует | Создать новый remote с уникальным именем и target URL |
| URL нельзя сконвертировать | Не менять remotes, показать ошибку |
| В repository нет remotes | Не создавать из пустого состояния; пользователь должен указать repository URL / подключить repository |
| Initial binding или metadata reload выставляет selected type | Не вызывать service mutation |

Инварианты:
- Один и тот же target URL не должен приводить к нескольким новым remotes.
- Переключение типа не должно менять branch/refspec.
- SSH mode должен включать `SshKeysSection`, HTTP mode должен включать token credentials block.

## 8. Точки интеграции и триггеры
- Изменение selected item в новом UI control вызывает ViewModel command.
- Команда вызывает service method из `App.axaml.cs` по текущему паттерну внешних команд.
- После успешного service result вызывается `ReloadGitMetadata()` и выставляется selected remote.
- Существующие `SyncNow`, `PullNow`, `PushNow`, `ConnectRepository` продолжают использовать `GitRemoteName`/`GitRemoteUrl`.

## 9. Изменения модели данных / состояния
- Новые persisted fields не нужны.
- Новые calculated/bindable fields:
  - список вариантов `HTTP`/`SSH` или enum-backed options;
  - selected connection type display/value;
  - optional `CanSwitchRemoteConnectionType`.
- Git repository config меняется локально через добавление remote, если target URL отсутствует.
- `Git.RemoteName` и `Git.RemoteUrl` в app config обновляются на выбранный remote после успешного переключения.
- UI automation state получает stable selectors:
  - новый remote type control, например `GitRemoteConnectionTypeComboBox`;
  - token auth container, например `TokenAuthSection`;
  - существующий `SshKeysSection` сохраняется.

## 10. Миграция / Rollout / Rollback
- Миграция persisted config не требуется.
- При первом запуске после обновления UI просто покажет текущий type выбранного remote.
- Rollback: удалить новый UI/command/service method; созданные пользователем remotes останутся в `.git/config`, потому что это пользовательская Git metadata, а не app schema.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - При единственном `origin` с HTTP URL пользователь может выбрать `SSH`; создается remote `origin-ssh` с `git@host:owner/repo.git`, выбирается он, `GitRemoteUrl` становится SSH, auth mode становится SSH.
  - При единственном `origin` с SSH URL пользователь может выбрать `HTTP`; создается remote `origin-http` с `https://host/owner/repo.git`, выбирается он, auth mode становится HTTP/token.
  - Если remote с target URL уже существует, новый remote не создается, а выбирается существующий.
  - Повторное переключение на уже существующий target не создает дубликаты.
  - Initial binding и `ReloadGitMetadata()` не создают alternate remote без явного действия пользователя.
  - Unsupported URL не меняет remote list и сообщает ошибку.
  - UI test проходит через окно настроек и проверяет доступность переключателя и смену auth UI.
- Какие тесты добавить/изменить:
  - `SettingsViewModelTests`: переключение selected connection type вызывает command/state update через fake service; selected remote/url/auth mode обновляются.
  - `SettingsViewModelTests`: initial binding/programmatic reload selected type не вызывает service mutation.
  - `BackupViaGitServiceTests`: temp repository с HTTP/SSH remotes, duplicate avoidance, unique name, unsupported URL.
  - `BackupViaGitServiceTests`: canonical comparison не создает дубликаты для `.git`/trailing slash/default port variants в рамках поддержанных форматов.
  - `MainWindowScenariosBase`: settings scenario для переключателя типа remote; page selectors в `MainWindowPage`.
  - Обновить `UnlimotionAutomationScenarioData` или test host setup так, чтобы headless scenario создавал локальный `.git` repository с одним remote; без этого UI-тест не доказывает создание/выбор alternate remote.
- Characterization tests / contract checks:
  - существующие тесты `BackupAuthMode_*` и `ReloadGitMetadata_*` должны продолжить проходить.
- Базовые замеры performance: не применимо, операция локальная и не должна добавлять сетевых вызовов.
- Команды для проверки:
  - `dotnet build src\Unlimotion.sln`
  - `dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowScenariosBase/*Settings*"`
  - `dotnet test src\Unlimotion.sln --no-build`
  - `git diff --check`
- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted UI test не запускается из-за окружения, выполнить ближайший доступный AppAutomation/Headless test project command и явно зафиксировать блокер.
  - Если full test suite блокируется lock-файлами работающего приложения, выполнить project-level targeted tests и явно указать заблокированный полный прогон.

## 12. Риски и edge cases
- URL conversion может быть неоднозначной для нестандартных Git URLs. Смягчение: поддержать только надежные форматы, остальные не менять.
- Добавление метода в `IRemoteBackupService` потребует обновить fake services во всех тестах. Смягчение: минимальный контракт с простым DTO.
- UI command может триггериться во время initial binding. Смягчение: не выполнять mutation, если selected type равен текущему calculated type или metadata еще не загружена.
- Programmatic update selected type после успешного switch или metadata reload может повторно вызвать команду. Смягчение: явный suppression guard покрыть regression-тестом.
- Созданный remote name может конфликтовать. Смягчение: helper unique name с suffix.
- AppAutomation test может требовать фактического `.git` repository в test host. Смягчение: если текущий host не создает `.git`, добавить локальную подготовку test data в рамках существующего test host pattern.

## 13. План выполнения
1. Добавить failing unit tests для service URL conversion / duplicate avoidance и ViewModel state update.
2. Добавить failing UI automation test и selectors для нового control.
3. Реализовать service contract и `BackupViaGitService` mutation без network.
4. Реализовать ViewModel properties/command integration и App command binding.
5. Обновить `SettingsControl.axaml` и локализацию.
6. Запустить targeted tests, затем build/full tests; исправить regressions.
7. Выполнить post-EXEC review и повторить затронутые проверки при исправлениях.

## 14. Открытые вопросы
Нет блокирующих вопросов.

Принятые решения:
- Для новых remote имен использовать `<selectedRemote>-http` / `<selectedRemote>-ssh` с числовым суффиксом при конфликте.
- HTTP target строить как `https://...`, даже если исходный URL был SSH.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - UI-поток настроек меняется через ViewModel и async service command, без длительной синхронной работы на UI-потоке.
  - Стабильные automation-id будут добавлены для нового control.
  - Перед завершением будут запущены `dotnet build` и `dotnet test` или зафиксирован блокер.
- Профиль: `ui-automation-testing`
- Выполненные требования профиля:
  - Добавляется/обновляется AppAutomation UI test для settings flow.
  - Тесты используют automation-id, а не текстовые/позиционные привязки.
  - Релевантный UI test будет запущен до финала или блокер будет явно указан.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/IRemoteBackupService.cs` | Добавить метод переключения типа remote и DTO результата | ViewModel нужен service-level Git metadata mutation |
| `src/Unlimotion/Services/BackupViaGitService.cs` | Реализовать URL conversion, duplicate lookup, remote creation | Реальная работа с `.git/config` |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | Добавить bindable state/command для HTTP/SSH selection | Управление UI state настроек |
| `src/Unlimotion/App.axaml.cs` | Подключить async command к service и status/error handling | Существующий паттерн external commands |
| `src/Unlimotion/Views/SettingsControl.axaml` | Добавить UI control для типа подключения и stable `TokenAuthSection` selector | Пользовательский доступ из окна настроек и надежный UI test |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | Английские строки | Локализация UI/status |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | Русские строки | Локализация UI/status |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | Regression tests ViewModel state | Проверка логики настроек |
| `src/Unlimotion.Test/BackupViaGitServiceTests.cs` | Regression tests Git remote mutation | Проверка Git metadata |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Selectors для нового UI | Stable automation |
| `tests/Unlimotion.UiTests.Headless/Tests/SettingsRemoteTypeHeadlessTests.cs` | UI scenario settings remote type switch | Обязательное UI coverage |
| `tests/Unlimotion.AppAutomation.TestHost/*` | Подготовка локального `.git` repository с одним remote для UI scenario | UI test должен доказать creation/selection alternate remote |
| `specs/2026-05-13-remote-auth-type-switch.md` | Журнал и EXEC результаты | QUEST audit |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Remote type | Только derived из URL/remote auth type | Явно переключается в настройках |
| Один remote | Пользователь вручную меняет URL/Git config | Можно создать alternate remote из UI |
| Duplicate target URL | Не применимо | Существующий remote выбирается, новый не создается |
| Auth UI | Следует выбранному URL/remote | Обновляется после переключения типа |
| Тесты | Есть SSH settings UI flow | Добавляется flow переключения HTTP/SSH remote |

## 18. Альтернативы и компромиссы
- Вариант: менять URL у текущего remote на месте.
  - Плюсы: меньше remotes.
  - Минусы: пользователь теряет старый тип подключения; противоречит запросу создать копию при отсутствии варианта.
  - Почему не выбран: запрос явно говорит про создание копии выбранного remote с другим типом подключения.
- Вариант: только менять `GitRemoteUrl` в app settings без `.git/config`.
  - Плюсы: проще.
  - Минусы: sync/pull/push используют selected remote; состояние UI и Git metadata расходятся.
  - Почему не выбран: фича именно про тип remote, а не только текстовое поле URL.
- Вариант: дать отдельные кнопки `Create HTTP copy` / `Create SSH copy`.
  - Плюсы: явное действие.
  - Минусы: хуже основной flow "сменить тип"; больше UI шума.
  - Почему выбранное решение лучше в контексте этой задачи: переключатель `HTTP`/`SSH` прямо выражает целевое состояние, а создание копии становится безопасной реализационной деталью.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, правила, ошибки, perf и state описаны. |
| C. Безопасность изменений | 11-13 | PASS | Миграция не нужна; rollback и edge cases описаны; destructive changes исключены. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, test plan, UI fixture и команды проверки указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План этапов есть; блокирующих вопросов нет; масштаб medium. |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client` и `ui-automation-testing` требования отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Задача ограничена переключением remote type в settings и созданием alternate remote. |
| 2. Понимание текущего состояния | 5 | Зафиксированы текущие ViewModel/service/UI/test точки. |
| 3. Конкретность целевого дизайна | 5 | Описан service contract, алгоритм duplicate avoidance, canonical URL comparison, UI и state update. |
| 4. Безопасность (миграция, откат) | 5 | Нет schema migration; rollback и недеструктивность remotes описаны. |
| 5. Тестируемость | 5 | Есть unit/service/UI tests и команды targeted/full verification. |
| 6. Готовность к автономной реализации | 5 | Нет открытых вопросов, план и file scope заданы. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: в spec добавлено правило не создавать remote при совпадении target type, уточнен controlled error для unsupported URL, зафиксированы stable automation-id и headless UI test requirement. После review добавлены suppression guard для initial binding/reload, canonical URL comparison, `TokenAuthSection` selector и обязательная `.git` fixture для UI scenario.
- Что осталось на решение пользователя: подтвердить переход в EXEC фразой `Спеку подтверждаю`.

## Approval
Ожидается фраза: "Спеку подтверждаю"

Подтверждено пользователем: "спеку подтверждаю".

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершенный значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор инструкций и кода | 0.86 | Нет финального подтверждения спеки | Создать и показать spec | Да | Нет | Центральный QUEST требует SPEC-first и explicit approval перед EXEC; изучены настройки, service и UI tests | `AGENTS.override.md`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion/Services/BackupViaGitService.cs`, `tests/Unlimotion.UiTests.Authoring/*` |
| SPEC | Подготовка спеки | 0.90 | Нет финального подтверждения спеки | Ожидать фразу `Спеку подтверждаю` | Да | Да, ожидается решение пользователя | Spec фиксирует outcome, алгоритм, тесты и safety boundaries для изменения remote type | `specs/2026-05-13-remote-auth-type-switch.md` |
| SPEC | Review fixes | 0.93 | Нет финального подтверждения спеки | Ожидать фразу `Спеку подтверждаю` | Да | Да, пользователь попросил внести изменения после review | Устранены review-находки: accidental mutation на binding/reload, нечеткие normalization rules, отсутствие token selector и необязательная UI fixture | `specs/2026-05-13-remote-auth-type-switch.md` |
| EXEC | Контекст реализации | 0.87 | Нет | Добавить regression tests | Нет | Да, пользователь подтвердил spec | Уточнены места изменений: команды настроек в `App.axaml.cs`, Git metadata в `BackupViaGitService`, headless host пока создает `SettingsViewModel` без backup service и потребует fixture | `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion/App.axaml.cs`, `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAppLaunchHost.cs` |
| EXEC | Реализация и тесты | 0.82 | Нужен compile/test сигнал | Запустить targeted build/tests | Нет | Нет | Добавлены DTO и service method для switch, canonical URL comparison, ViewModel/App command, UI toggles, `TokenAuthSection`, service/ViewModel/UI regression tests и headless `.git` fixture | `src/Unlimotion.ViewModel/IRemoteBackupService.cs`, `src/Unlimotion.ViewModel/RemoteConnectionTypeSwitchResult.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `tests/Unlimotion.AppAutomation.TestHost/*`, `tests/Unlimotion.UiTests.*` |
| EXEC | Targeted verification | 0.90 | Нет | Запустить full build/test | Нет | Нет | Пройдены targeted service/ViewModel tests и новый headless UI scenario; UI использует toggle-кнопки с selected state, headless action запускает bound command через VM после проверки automation controls | `tests/Unlimotion.UiTests.Headless/Tests/SettingsRemoteTypeHeadlessTests.cs`, `src/Unlimotion/Views/SettingsControl.axaml`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` |
| EXEC | Финальная проверка | 0.88 | Нет | Подготовить итог | Нет | Нет | Отдельные build affected projects прошли, все headless UI tests прошли, targeted unit/service tests прошли; full solution build и full `Unlimotion.Test` не завершились за 5 минут и были остановлены как зависшие процессы | `src/Unlimotion.Test/Unlimotion.Test.csproj`, `src/Unlimotion/Unlimotion.csproj`, `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj` |
| EXEC | Follow-up refresh regression | 0.91 | Нет | Обновить PR | Нет | Да, пользователь указал на проблему refresh при пустом URL | Найден и исправлен fallback path: при пустом `TaskStorage.Path` backup service теперь использует текущий `FileStorage`; добавлены service/ViewModel/headless UI tests для refresh empty remote URL | `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `src/Unlimotion/Views/SettingsControl.axaml`, `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAppLaunchHost.cs`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`, `tests/Unlimotion.UiTests.Headless/Tests/SettingsRemoteTypeHeadlessTests.cs` |
