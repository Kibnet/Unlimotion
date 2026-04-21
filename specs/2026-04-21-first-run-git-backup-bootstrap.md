# Первый запуск и bootstrap Git backup

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`; контекст `testing-dotnet`
- Владелец: Codex
- Масштаб: large
- Целевой релиз / ветка: текущая ветка `codex/end-user-settings-page`
- Ограничения: не менять формат task-файлов; не выполнять destructive overwrite локальных задач; Git-операции должны оставаться вне UI-потока; пользовательское подтверждение обязательно перед объединением непустой локальной папки с непустым remote repository
- Связанные ссылки: запрос пользователя от 2026-04-21 про первый запуск desktop и подключение пустого GitHub repository

## 1. Overview / Цель
Сделать первый запуск desktop-версии понятным и безопасным: явно показать фактическую папку задач по умолчанию, добавить подсказку по созданию приватного GitHub repository и SSH-ключа, а подключение backup repository должно уметь bootstrap-ить пустой remote из уже существующей папки задач и безопасно объединять непустой remote с локальными задачами только после подтверждения.

## 2. Текущее состояние (AS-IS)
- `App.Init()` создаёт `TaskStorageSettings`, но если `TaskStorage.Path` пустой, оставляет пустое значение в конфиге и UI, хотя фактический fallback storage существует.
- `TaskStorageFactory.CreateFileStorage()` использует `GetStoragePath(path)`, а `FileStorage` при пустом path падает к `TaskStorageFactory.DefaultStoragePath`, затем к `"Tasks"`.
- `SettingsViewModel.TaskStoragePathTooltip` уже умеет показывать фактический fallback path, но само поле `TaskStoragePath` остаётся пустым.
- Подсказка в backup секции короткая: “Подключите Git-репозиторий...”; пошаговой инструкции для GitHub private repository, SSH-key generation/copy и добавления ключа нет.
- `BackupViaGitService.CloneOrUpdateRepo()` при отсутствии `.git` пытается `Repository.Clone(remoteUrl, repositoryPath)`.
- `Repository.Clone` не подходит для текущего сценария, если папка задач уже существует и содержит task-файлы: Git не клонирует в непустую папку.
- Для SSH с выбранным ключом сервис использует `git clone/fetch/push` через `GIT_SSH_COMMAND`, но нет bootstrap flow для `git init` + remote + first push.
- В `App.axaml.cs` уже есть `INotificationManagerWrapper.Ask`, которым можно показывать подтверждение.

## 3. Проблема
На первом запуске приложение скрывает фактическую локальную папку задач и не объясняет GitHub/SSH onboarding, а подключение Git backup предполагает clone-flow, который ломается для пустого remote repository и непустой локальной папки с уже созданными задачами.

## 4. Цели дизайна
- Разделение ответственности:
  - `SettingsViewModel` показывает onboarding text/state и derived UI-флаги.
  - `BackupViaGitService` определяет состояние remote/local repository и выполняет Git bootstrap/connect.
  - `App.axaml.cs` показывает пользовательское подтверждение и запускает выбранный connect-flow.
- Повторное использование: сохранить существующие `Pull()`, `Push()`, `ReloadGitMetadata()` и watcher disable/enable patterns.
- Тестируемость: вынести decision points в small methods/contract, чтобы покрыть empty remote, non-empty remote, non-empty local folder, default path initialization.
- Консистентность: UI должен показывать тот же path, который реально используется при file storage.
- Обратная совместимость: существующие конфиги продолжают работать; уже заполненный `TaskStorage.Path` не перезаписывается.

## 5. Non-Goals (чего НЕ делаем)
- Не создаём repository через GitHub API из приложения; пользователь создаёт private repository в GitHub UI.
- Не добавляем OAuth/GitHub token onboarding.
- Не меняем storage format task-файлов.
- Не реализуем сложный Git conflict resolver внутри приложения; при conflict показываем ошибку и оставляем репозиторий для ручного исправления.
- Не меняем scheduling jobs за пределами необходимости корректного bootstrap.
- Не меняем уже созданный публичный API без минимально нужного расширения `IRemoteBackupService`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `App.Init()` / helper -> при первом запуске, если локальный storage mode и `TaskStorage.Path` пустой, записать фактический default path в конфиг до создания storage. Для desktop path должен совпадать с текущим fallback `LocalApplicationData/Unlimotion/Tasks`.
- `SettingsViewModel.cs` -> добавить текстовые properties для GitHub/SSH onboarding hint и/или флаг показа расширенной подсказки.
- `SettingsControl.axaml` -> добавить компактную подсказку в backup-блок:
  - создать новый private repository на GitHub без README/license/gitignore, если нужно загрузить уже созданные локальные задачи;
  - нажать “Создать” в SSH-ключах или выбрать существующий ключ;
  - скопировать публичный ключ и добавить его в GitHub: repository Deploy keys с write access или профиль SSH keys;
  - вставить SSH URL вида `git@github.com:owner/repo.git`;
  - нажать “Подключить репозиторий”.
- `IRemoteBackupService` -> расширить контракт для предварительной оценки подключения и выполнения connect с подтверждением:
  - `BackupRepositoryConnectPreview PreviewConnectRepository()`
  - `void ConnectRepository(bool allowMergeWithNonEmptyRemote)`
  - или эквивалентные имена, если локальный стиль подскажет лучше.
- `BackupViaGitService.cs` -> заменить `CloneOrUpdateRepo()` внутри команды на новый bootstrap-aware flow, оставив old method как compatibility wrapper при необходимости.
- `App.axaml.cs` -> перед connect вызвать preview; если remote непустой и локальная папка содержит task-файлы без `.git`, показать confirmation через `Ask`; при согласии вызвать connect с allow flag.
- `SettingsViewModelTests.cs` и новые/существующие service tests -> покрыть default path display/persistence и connect decision logic.

### 6.2 Детальный дизайн Git flow
Термины:
- local path: фактическая папка задач из `TaskStorage.Path`.
- local repository exists: `Repository.IsValid(local path)`.
- local task folder non-empty: в папке есть файлы/каталоги кроме `.git` и служебных пустых директорий.
- remote empty: `ls-remote`/LibGit2 remote refs возвращает 0 refs.
- remote non-empty: remote refs есть.

Flow `PreviewConnectRepository()`:
1. Resolve repository path and remote URL.
2. If local path is already valid Git repository: preview action is `PullExistingRepository`, no confirmation.
3. If local path is not valid Git repository:
   - detect local non-empty folder;
   - detect whether remote has refs.
4. If remote is empty:
   - preview action is `InitializeLocalAndPush`;
   - no confirmation required, because local tasks become initial remote contents.
5. If remote is non-empty and local folder is empty:
   - preview action is `FetchIntoEmptyLocalFolder`;
   - no confirmation required.
6. If remote is non-empty and local folder is non-empty:
   - preview action is `MergeNonEmptyLocalWithRemote`;
   - confirmation required with warning that local task folder will be initialized as Git repository and merged with remote contents.

Flow `ConnectRepository(allowMergeWithNonEmptyRemote)`:
1. For existing local Git repository: run `Pull()`.
2. For empty remote:
   - `git init` / `Repository.Init(local path)`;
   - create or update remote `Git.RemoteName` pointing to `Git.RemoteUrl`;
   - ensure canonical local branch `Git.PushRefSpec` exists and is checked out;
   - stage current task files;
   - create initial commit if dirty;
   - push branch to remote.
3. For non-empty remote + empty local folder:
   - initialize local repository in the existing path;
   - add remote;
   - fetch remote;
   - checkout configured branch from remote, creating local tracking branch where needed.
4. For non-empty remote + non-empty local folder:
   - if `allowMergeWithNonEmptyRemote` is false, abort with a typed/clear result;
   - initialize local repository;
   - add remote;
   - stage and commit local task files as local baseline if dirty;
   - fetch remote;
   - merge remote branch into local branch with `MergeOptions`;
   - if clean, push merged branch when local tip differs from remote.
5. After successful connect, update metadata: remotes, refs, auth type, `GitRemoteUrl`, `GitRemoteName`, `GitPushRefSpec`.

SSH handling:
- If `Git.RemoteUrl` is SSH and `SshPrivateKeyPath` is set, all remote-probing/fetch/push operations that cannot reliably use LibGit2 credentials must use the existing `RunGitCommandWithConfiguredSshKey` pattern.
- For non-SSH, use LibGit2Sharp credentials provider as today.
- Any helper that shells out to `git` must set `GIT_TERMINAL_PROMPT=0`.

Branch rules:
- UI continues to show canonical `GitPushRefSpec` such as `refs/heads/main`.
- For Git CLI commands that need a short branch name, derive `main` from `refs/heads/main`.
- If `GitPushRefSpec` is empty, keep existing fallback from `GitBranch`.

Storage default path:
- Default path written into `TaskStorage.Path` must be the actual path used by file storage, not a display-only placeholder.
- Existing non-empty `TaskStorage.Path` must be preserved.

## 7. Бизнес-правила / Алгоритмы
| Сценарий | Условие | Поведение |
| --- | --- | --- |
| Первый запуск desktop | `TaskStorage.Path` пустой, local mode | Конфиг и поле настроек получают фактический default path |
| Уже настроенный path | `TaskStorage.Path` непустой | Значение не перезаписывается |
| GitHub onboarding | Backup block открыт | Пользователь видит краткие шаги private repo + SSH key + URL |
| Empty remote + local tasks | remote refs отсутствуют, local folder непустая | `git init`, remote, branch, initial commit, push без предупреждения |
| Non-empty remote + empty local folder | remote refs есть, local folder пустая | init/fetch/checkout без предупреждения |
| Non-empty remote + local tasks | remote refs есть, local folder непустая | warning + confirmation; без подтверждения connect отменяется |
| Existing local Git repo | `.git` валиден | connect делает pull/update без bootstrap |
| Merge conflict | merge remote + local дал conflicts | Показать понятную ошибку, не скрывать conflict |

## 8. Точки интеграции и триггеры
- `App.Init()` до `_storageFactory.CreateFileStorage(...)` должен обеспечить persisted default path.
- `SettingsViewModel` constructor должен видеть уже заполненный `TaskStorage.Path`.
- `CloneCommand` / кнопка “Подключить репозиторий” должна перейти на preview + optional confirm + connect.
- После connect нужно вызвать `settings.ReloadGitMetadata()` и `WireSettingsToCurrentStorage(settings)`/storage reconnect при смене фактической папки, если implementation действительно меняет storage path.
- Scheduler должен продолжить работать через существующие `GitPullJob`/`GitPushJob`.

## 9. Изменения модели данных / состояния
- Новых persisted fields не требуется.
- `TaskStorage.Path` может начать заполняться на первом запуске вместо пустого значения.
- `IRemoteBackupService` получает новый preview/connect контракт.
- Возможны новые DTO/enum в `Unlimotion.ViewModel`, например:
  - `BackupRepositoryConnectPreview`
  - `BackupRepositoryConnectAction`

## 10. Миграция / Rollout / Rollback
- При запуске с пустым local path приложение запишет default path один раз.
- Существующие конфиги с пользовательским path не меняются.
- Rollback default-path части: перестать записывать path, но не очищать уже записанный пользовательский путь.
- Rollback Git bootstrap: вернуть clone-only connect, сохранив тесты отдельно отключёнными нельзя; откат должен включать удаление/адаптацию regression-тестов.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - На первом запуске desktop в поле “Папка с данными” отображается фактический default path, а не пустая строка.
  - Backup block содержит понятную подсказку по созданию private GitHub repo, SSH-ключа, добавлению public key и вставке SSH URL.
  - Empty GitHub repository с URL `git@github.com:Kibnet/TestTasks.git` подключается к уже существующей local task folder через init + first push.
  - Для non-empty remote + non-empty local task folder показывается предупреждение и требуется подтверждение.
  - При отказе от подтверждения Git connect не меняет local folder/repository.
  - При подтверждении non-empty remote + local tasks выполняется merge-flow; conflicts показываются пользователю.
  - Git operations не блокируют UI thread.
- Какие тесты добавить/изменить:
  - Unit tests для default path initialization/persistence.
  - Unit tests для preview decision matrix: existing repo, empty remote, non-empty remote + empty local, non-empty remote + non-empty local.
  - Unit tests для `CloneCommand` confirmation branching через `NotificationManagerWrapperMock` или выделенный coordinator.
  - Integration-ish temp-directory tests для local `Repository.Init`, remote bare repository empty/non-empty, initial push and merge behavior.
  - Regression test для SSH URL branch/ref conversion helpers без реального GitHub.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln --no-build`

## 12. Риски и edge cases
- GitHub empty private repo over SSH cannot be tested against real GitHub locally; core behavior should be covered with local bare repository and SSH-specific command construction separately.
- Merge conflicts are possible; implementation must surface them and not silently overwrite task files.
- LibGit2Sharp may not handle SSH private key exactly like Git CLI; keep existing `GIT_SSH_COMMAND` path for configured SSH key.
- Writing default path to config changes first-run persisted state; must preserve existing explicit user path.
- Git clone into non-empty directory is intentionally avoided; implementation should use init/fetch/merge.
- Remote default branch may be `main`, while current defaults may be `refs/heads/master`; UI already lets user set canonical branch. Bootstrap should use `GitPushRefSpec`, not assume GitHub default.

## 13. План выполнения
1. Добавить regression tests для default path persistence and display.
2. Добавить DTO/enum preview contract в ViewModel layer and fake service updates.
3. Добавить service decision tests with temp folders/local bare repositories.
4. Реализовать `PreviewConnectRepository()` in `BackupViaGitService`.
5. Реализовать bootstrap connect paths:
   - existing repo pull;
   - empty remote initial push;
   - non-empty remote empty folder fetch/checkout;
   - non-empty remote non-empty folder guarded merge.
6. Обновить `CloneCommand` in `App.axaml.cs` to preview, ask confirmation when needed, then connect.
7. Добавить onboarding hint in `SettingsViewModel`/`SettingsControl.axaml`.
8. Запустить targeted tests, build, full tests.
9. Выполнить post-EXEC review, including manual sanity against XAML text and no destructive Git paths.

## 14. Открытые вопросы
Нет блокирующих вопросов. Принятые допущения:
- Private GitHub repository создаётся пользователем вручную, приложение только объясняет шаги.
- Для write access SSH public key можно добавить либо в GitHub profile SSH keys, либо в repository Deploy keys с write access.
- Для non-empty local + non-empty remote используем init/fetch/merge, а не literal `git clone`, потому что clone в непустую папку невозможен.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - Длительные Git операции должны выполняться через `Task.Run`, как существующие clone/pull/push.
  - Confirmation dialog используется только через существующий UI notification wrapper.
  - Binding changes are localized to settings screen.
  - План проверки включает `dotnet build` и `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/App.axaml.cs` | default path initialization; CloneCommand preview/confirm/connect | Первый запуск и confirmation flow |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | onboarding hint properties, possibly default-path state refresh | UI state/settings text |
| `src/Unlimotion.ViewModel/IRemoteBackupService.cs` | preview/connect contract | Typed connect flow |
| `src/Unlimotion.ViewModel/TaskStorageSettings.cs` | likely no schema change; only used constants/defaults if needed | Preserve config model |
| `src/Unlimotion/Services/BackupViaGitService.cs` | Git remote probing, init/fetch/merge/push bootstrap | Core Git behavior |
| `src/Unlimotion/Views/SettingsControl.axaml` | onboarding hint text | User guidance |
| `src/Unlimotion.Test/*` | regression and integration-ish tests | Behavioral safety |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Первый запуск path | UI/config path пустой, storage fallback скрыт | UI/config показывает фактический default path |
| GitHub onboarding | Короткая общая подсказка | Шаги private repo + SSH key + public key placement + SSH URL |
| Empty remote connect | Clone into task folder fails | Init local repo, commit tasks, push initial branch |
| Non-empty remote + local tasks | Clone fails or unclear behavior | Warning, confirmation, init/fetch/merge |
| Existing local repo | Pull/update | Pull/update remains |

## 18. Альтернативы и компромиссы
- Вариант: всегда клонировать в новую папку и потом копировать локальные задачи.
- Плюсы: ближе к стандартному Git clone.
- Минусы: меняет storage path, требует миграции/копирования задач, повышает риск потери связи с текущим watcher/storage.
- Почему выбранное решение лучше: init/fetch/merge работает в существующей папке задач и не требует переносить storage.

- Вариант: сервис сам показывает confirmation.
- Плюсы: меньше App coordination code.
- Минусы: Git service получает UI responsibility и хуже тестируется.
- Почему выбранное решение лучше: preview DTO + App confirmation сохраняет разделение ответственности.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, Git flow, storage path, rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Есть confirmation, no overwrite, rollback и conflict handling. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, тесты и команды указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План есть, блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client` требования отражены. |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Сценарий первого запуска и Git bootstrap разложены на проверяемые цели. |
| 2. Понимание текущего состояния | 5 | Указаны конкретные current flow и limitations clone into non-empty folder. |
| 3. Конкретность целевого дизайна | 5 | Есть preview/connect contract and Git decision matrix. |
| 4. Безопасность (миграция, откат) | 5 | Подтверждение перед merge, no overwrite, rollback описан. |
| 5. Тестируемость | 5 | Есть unit/integration-ish test plan and commands. |
| 6. Готовность к автономной реализации | 5 | Открытых вопросов нет, план пошаговый. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: учтено, что literal clone into non-empty folder невозможен; выбран init/fetch/merge flow. Добавлен explicit confirmation boundary and remote emptiness decision matrix.
- Что осталось на решение пользователя: требуется подтверждение спеки фразой `Спеку подтверждаю`.

## Approval
Подтверждено пользователем фразой: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор контекста первого запуска и Git connect flow | 0.9 | Нет | Создать спецификацию | Да | Да, требуется подтверждение перед EXEC | Изменение затрагивает storage init, Git bootstrap and confirmation UX, поэтому нужен SPEC-first | `specs/2026-04-21-first-run-git-backup-bootstrap.md` |
| SPEC | Post-SPEC review | 0.9 | Нет | Ожидать фразу `Спеку подтверждаю` | Да | Да, approval ещё не получен | Спека прошла linter/rubric; clone into non-empty заменён безопасным init/fetch/merge design | `specs/2026-04-21-first-run-git-backup-bootstrap.md` |
| EXEC | Regression tests red-step | 0.9 | Нет | Реализовать preview/connect contract | Нет | Нет | Добавлены тесты default path и Git bootstrap; build падает на отсутствующих `PreviewConnectRepository`/`ConnectRepository`, что подтверждает red-step | `src/Unlimotion.Test/SettingsViewModelTests.cs`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.ViewModel/IRemoteBackupService.cs`, `src/Unlimotion.ViewModel/BackupRepositoryConnectPreview.cs` |
| EXEC | Implementation and verification | 0.9 | Нет | Передать результат пользователю | Нет | Нет | Реализованы persisted default task path, GitHub/SSH onboarding hint, preview/confirmation connect flow, empty-remote bootstrap push and guarded non-empty remote merge; `dotnet build src\Unlimotion.sln --no-restore /clp:ErrorsOnly` and `dotnet test src\Unlimotion.sln --no-build` pass (`163/163`) | `src/Unlimotion/App.axaml.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion.ViewModel/IRemoteBackupService.cs`, `src/Unlimotion.ViewModel/BackupRepositoryConnectPreview.cs`, `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs` |
