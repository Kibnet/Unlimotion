# Auto pull selected task directory

## 0. Метаданные
- Тип (профиль): delivery-task; profiles: `dotnet-desktop-client`, `ui-automation-testing`; context: `testing-dotnet`
- Владелец: Unlimotion desktop / local task storage / Git backup
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения:
  - Использовать central instruction stack из `C:\Users\Kibnet\.codex\agents`.
  - На фазе SPEC менять только этот spec-файл.
  - До фразы `Спеку подтверждаю` не менять кодовые файлы.
  - Не создавать Git-репозиторий в выбранной папке автоматически.
  - Не клонировать удаленный репозиторий для папки без `.git`.
  - Не выполнять Git pull на UI-потоке.
  - Для UI-facing изменения добавить или обновить UI test coverage и запустить релевантные UI tests.
- Связанные ссылки:
  - `AGENTS.md`
  - `AGENTS.override.md`
  - `C:\Users\Kibnet\.codex\agents\AGENTS.md`
  - `C:\Users\Kibnet\.codex\agents\templates\specs\_template.md`
  - `C:\Users\Kibnet\.codex\agents\instructions\governance\routing-matrix.md`
  - `C:\Users\Kibnet\.codex\agents\instructions\core\quest-governance.md`
  - `C:\Users\Kibnet\.codex\agents\instructions\core\quest-mode.md`
  - `C:\Users\Kibnet\.codex\agents\instructions\contexts\testing-dotnet.md`
  - `C:\Users\Kibnet\.codex\agents\instructions\profiles\dotnet-desktop-client.md`
  - `C:\Users\Kibnet\.codex\agents\instructions\profiles\ui-automation-testing.md`

## 1. Overview / Цель
При переключении локального каталога задач в настройках приложение должно перед загрузкой задач проверить выбранную папку: если в ней уже есть валидный Git-репозиторий с remote, нужно попытаться подтянуть изменения из remote; если Git-репозитория или remote там нет, не делать никаких Git-действий.

Outcome contract:
- Success means: при применении другого локального `TaskStorage.Path` существующий Git-репозиторий с remote в этой папке получает `pull` до создания нового `FileStorage`; папка без `.git` или без remote продолжает подключаться как обычное локальное хранилище без инициализации, clone или ошибок Git.
- Итоговый артефакт / output: сервисный helper/контракт для условного pull, интеграция в local storage connect flow, regression tests и UI test coverage.
- Stop rules: не завершать EXEC, если targeted Git/service tests или релевантный UI test падают; если full test run заблокирован окружением, зафиксировать команду, ошибку и nearest valid verification.

## 2. Текущее состояние (AS-IS)
- Настройки локального каталога задач живут в `src/Unlimotion.ViewModel/SettingsViewModel.cs`: `TaskStoragePath` сохраняется в `TaskStorage:Path`.
- UI настроек живет в `src/Unlimotion/Views/SettingsControl.axaml`; поле пути и кнопка выбора папки меняют настройку, а `ConnectCommand` применяет хранилище.
- `App.SetupSettingsCommands` в `src/Unlimotion/App.axaml.cs`:
  - `BrowseTaskStoragePathCommand` выбирает папку и сохраняет путь;
  - `ConnectCommand` вызывает `PrepareFileStoragePathAsync`, затем `_storageFactory.SwitchStorage(...)` и `MainWindowViewModel.Connect()`.
- `TaskStorageFactory.SwitchStorage` создает `FileStorage` по текущему `TaskStorage.Path`.
- `BackupViaGitService.Pull()` уже умеет делать fetch/merge для валидного репозитория и no-op, если `Repository.IsValid(path)` возвращает `false`.
- Для существующего репозитория `ConnectRepository` уже вызывает `EnsureGitSelectionFromLocalRepository(...)` перед `Pull()`, чтобы подстроить `RemoteName` и `PushRefSpec` под локальный репозиторий.
- Сейчас смена локального каталога задач не запускает pull автоматически; пользователь должен отдельно пользоваться Git backup controls.
- В репозитории есть TUnit tests в `src/Unlimotion.Test` и UI automation suites в `tests/Unlimotion.UiTests.Headless` / `tests/Unlimotion.UiTests.FlaUI`.

## 3. Проблема
При выборе другой папки задач, которая уже является Git-репозиторием, приложение может загрузить локальные файлы до подтягивания remote-изменений. Корневая проблема: local storage connect flow не синхронизирует существующий Git-репозиторий выбранного каталога перед созданием нового `FileStorage`.

## 4. Цели дизайна
- Разделение ответственности: проверка Git-репозитория и pull остаются в backup service; UI command только вызывает сервис до переключения storage.
- Повторное использование: использовать существующую логику `Pull()` и selection alignment для локального репозитория, а не дублировать Git merge logic.
- Тестируемость: покрыть no-op для папки без Git и pull для существующего repo.
- Консистентность: не менять визуальный flow настроек и существующие automation ids.
- Обратная совместимость: persisted settings и task JSON не меняются.

## 5. Non-Goals (чего НЕ делаем)
- Не инициализируем Git в новой папке.
- Не клонируем remote в папку без Git-репозитория.
- Не меняем UI настройки remote/credentials.
- Не меняем scheduler pull/push policy.
- Не меняем формат задач или миграции.
- Не добавляем новые диалоги подтверждения.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `IRemoteBackupService` -> добавить метод условного pull для текущего `TaskStorage.Path`, например `PullExistingRepository()` или `TryPullExistingRepository()`.
- `BackupViaGitService` -> реализовать метод:
  - вычислить repository path из текущего `TaskStorage.Path`;
  - если `Repository.IsValid(path) == false`, сразу вернуться без side effects;
  - если репозиторий валиден, но в нём нет remote, сразу вернуться без side effects;
  - если репозиторий валиден и remote есть, синхронизировать `RemoteName`/`PushRefSpec` с локальным repo и вызвать существующий `Pull()`.
- `App.axaml.cs` -> в local branch of `ConnectCommand` вызвать условный pull после `PrepareFileStoragePathAsync(settings.TaskStoragePath)` и до `_storageFactory.SwitchStorage(...)`.
- `SettingsViewModel` -> обязательно обновить metadata/conflict status после conditional pull перед storage switch; не добавлять persisted state.
- Tests -> добавить service-level regression и UI-facing coverage по существующим repository patterns.

### 6.2 Детальный дизайн
- Flow:
  1. Пользователь выбирает или вводит новый локальный каталог задач.
  2. Пользователь применяет локальное хранилище через существующую команду подключения.
  3. `ConnectCommand` переводит storage state в `Connecting`.
  4. Для local mode выполняется подготовка пути.
  5. Если нормализованный выбранный path совпадает с текущим подключенным local storage path, conditional pull не запускается и используется existing reconnect behavior.
  6. Если выбран другой local path, backup service получает шанс выполнить conditional pull:
     - нет валидного Git repo -> no-op;
     - валидный Git repo без remote -> no-op;
     - валидный Git repo с remote -> align local Git selection and call `Pull()`.
  7. После conditional pull приложение обновляет Git metadata/conflict status.
  8. Если pull оставил repository в conflict mode, приложение входит в существующий conflict resolver flow и не продолжает normal storage reload до завершения разрешения конфликтов.
  9. Если conflict mode не обнаружен, создается новое `FileStorage`, и `MainWindowViewModel.Connect()` загружает уже обновленные файлы.
- API/output contract:
  - Метод conditional pull не должен создавать репозиторий или remote.
  - Метод должен быть безопасен для пустого/невалидного пути и папки без `.git`.
  - Git pull должен использовать существующие credentials/settings and SSH handling.
  - Если pull приводит к conflict mode, существующий conflict handling остается источником истины; normal storage reload must stop before loading conflicted files.
- Error handling:
  - Папка без Git repo не считается ошибкой.
  - Ошибки реального pull для валидного repo обрабатываются как текущие ошибки подключения storage: state `Error` и toast через `ConnectStorageFailed`, либо через существующий Git error toast, если `Pull()` уже проглатывает ошибку. В EXEC нужно сохранить текущий стиль обработки ошибок без новых UI modal.
- Производительность:
  - Git operation выполняется через `Task.Run` или эквивалентно вне UI thread.
  - Для папки без repo проверка должна быть быстрой и без network.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Условный pull запускается только для local storage mode.
- Условный pull запускается при применении выбранного local path, а не при простом открытии Settings.
- Условный pull запускается только если выбранный local path отличается от текущего подключенного local storage path после нормализации абсолютного пути; повторное подключение той же папки не запускает auto-pull.
- Если выбранный path невалиден как Git repo, Git layer ничего не меняет.
- Если выбранный path валиден как Git repo:
  - preferred remote: текущий `Git.RemoteName`, если он есть в repo; иначе `origin`; иначе первый remote;
  - preferred branch/ref: текущий `Git.PushRefSpec`, если он есть; иначе current local branch; иначе существующая fallback-логика `main`/`master`/first ref.
- Если repo валиден, но remote отсутствует, метод не должен создавать remote; допустим no-op с сохранением дальнейшего подключения локального storage.
- Pull before storage load должен предшествовать `TaskStorageFactory.SwitchStorage`.
- Conflict detection after pull должен предшествовать `TaskStorageFactory.SwitchStorage`; при active conflict normal switch/load не выполняется.

## 8. Точки интеграции и триггеры
- `App.SetupSettingsCommands` / `ConnectCommand`: основной триггер при применении local storage path.
- `BackupViaGitService`: условный Git pull и no-op behavior.
- `SettingsViewModel.ReloadGitMetadata()`: после conditional pull обязательно обновить remote/ref metadata и conflict status перед `SwitchStorage`.
- Existing conflict resolver integration: если `settings.IsConflictResolutionMode` после metadata reload, войти в conflict resolution flow и остановить normal connect flow.
- UI tests: сценарий Settings/local storage connect должен остаться рабочим и покрывать пользовательский flow через стабильные selectors.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Runtime state:
  - storage remains `Connecting` while conditional pull runs;
  - если conditional pull приводит к conflict mode, storage state must not be marked as normally connected to the new path until conflicts are resolved;
  - после успешного no-op/pull existing flow возвращается к existing connected behavior.
- Git state:
  - только existing Git repo может быть изменен через fetch/merge/stash existing `Pull()` behavior;
  - папка без `.git` не получает `.git` и remote metadata.

## 10. Миграция / Rollout / Rollback
- First launch behavior: unchanged for default folder without Git repo.
- Backward compatibility: existing settings file remains valid.
- Rollout: feature ships as small behavior change in local storage connect flow.
- Rollback: remove conditional pull call and service method; no data migration rollback needed.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. Applying a different local task directory that is not a Git repo performs no Git initialization/clone/pull and still connects local storage.
  2. Applying a different local task directory that is a valid Git repo with remote attempts remote pull before `FileStorage` loads tasks.
  3. Existing repo remote/branch selection is aligned with the selected repository before pull.
  4. Conditional pull runs off the UI thread.
  5. Server storage mode does not trigger conditional Git pull.
  6. Browse-only path selection does not unexpectedly switch storage or load tasks.
  7. Existing manual backup connect/sync/pull/push flows continue to work.
  8. UI test coverage exists for the Settings flow affected by local path switching.
  9. Reconnecting the already connected local task directory does not trigger automatic pull.
  10. If automatic pull leaves Git conflicts, conflict resolution mode opens and normal `FileStorage` reload for the selected path is deferred.
- Какие тесты добавить/изменить:
  - `src/Unlimotion.Test/BackupViaGitServiceTests.cs`:
    - `PullExistingRepository_DoesNothing_WhenTaskFolderIsNotGitRepository`
    - `PullExistingRepository_DoesNothing_WhenRepositoryHasNoRemote`
    - `PullExistingRepository_PullsRemoteChanges_WhenTaskFolderIsExistingRepository`
    - `PullExistingRepository_SelectsRepositoryRemoteAndBranch_WhenSettingsAreStale`
  - `src/Unlimotion.Test/SettingsViewModelTests.cs` or App command-level/integration test:
    - local storage connect invokes conditional pull before switching storage; this ordering coverage is mandatory, not optional;
    - server mode does not invoke conditional pull;
    - reconnecting the same local path does not invoke conditional pull;
    - conflict mode after conditional pull stops normal storage reload.
  - `tests/Unlimotion.UiTests.Headless` / shared authoring:
    - add or extend Settings flow smoke to cover local storage path switching/apply behavior through automation ids.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/BackupViaGitServiceTests/*PullExistingRepository*"`
  - `dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*Settings*"`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln` или принятый full-run, если solution runner стабилен в окружении.
- Stop rules для validation loops:
  - targeted service tests must pass;
  - relevant UI test must pass or exact environment blocker must be reported;
  - full-run should be attempted after targeted checks.

## 12. Риски и edge cases
- Risk: pull happens after storage load and UI shows stale tasks.
  - Mitigation: call conditional pull before `SwitchStorage`.
- Risk: folder without repo gets `.git` by accident.
  - Mitigation: service method exits on `Repository.IsValid(path) == false`; test asserts `.git` is absent.
- Risk: stale global Git settings point to remote/branch absent in selected repo.
  - Mitigation: reuse repository-local selection alignment before `Pull()`.
- Risk: valid repo without remote causes noisy error on ordinary folder switch.
  - Mitigation: treat no remote as no-op in conditional method, while manual Pull can keep current behavior.
- Risk: network/merge blocks UI.
  - Mitigation: run conditional pull in background task from command.
- Risk: pull conflict appears while storage is switching.
  - Mitigation: require metadata/conflict reload before `SwitchStorage`; if conflict mode is active, enter resolver and skip normal storage reload.
- Risk: auto-pull runs too often on ordinary reconnect of the same folder.
  - Mitigation: compare normalized selected path with current connected local storage path and only auto-pull when the user applies a different local directory.

## 13. План выполнения
1. Service contract:
   - add conditional pull method to `IRemoteBackupService`;
   - implement in `BackupViaGitService` using existing repository path resolution, local repo selection alignment and `Pull()`.
2. App integration:
   - call conditional pull in local `ConnectCommand` before `SwitchStorage`;
   - compare selected local path with current connected local storage path and skip conditional pull for the same path;
   - reload Git metadata after conditional pull and stop normal connect flow on conflict mode;
   - keep browse-only behavior unchanged;
   - preserve existing conflict resolver entry point.
3. Regression tests:
   - add service tests for no-repo no-op, no-remote no-op and existing repo pull;
   - add command/integration ordering test for pull before storage switch;
   - add same-path skip and conflict-stop coverage; if direct command seam is unavailable, use an integration test instead of making this coverage optional.
4. UI coverage:
   - add/update relevant Headless Settings flow test using existing AppAutomation pattern.
5. Validation:
   - run targeted service/UI tests, build, then full available test run.
   - perform post-EXEC review and update this journal.

## 14. Открытые вопросы
Не применимо. Неоднозначность по триггеру решена консервативно: "выбрать другой каталог" трактуется как применение local storage path через существующую команду подключения, причем auto-pull запускается только если выбранный path отличается от текущего подключенного local storage path. Simple browse only changes the saved path and does not reload storage.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`; context `testing-dotnet`.
- Выполненные требования профиля:
  - Long-running Git operation is designed off UI thread.
  - Existing UI selectors should remain stable.
  - UI-facing behavior requires UI test coverage.
  - TUnit/Microsoft.Testing.Platform targeted syntax uses `--treenode-filter`.
  - `dotnet build` and full test attempt are part of validation.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-05-12-auto-pull-selected-task-directory.md` | Рабочая спецификация и журнал | Central QUEST contract |
| `src/Unlimotion.ViewModel/IRemoteBackupService.cs` | Add conditional pull method | Service boundary for selected existing repo |
| `src/Unlimotion/Services/BackupViaGitService.cs` | Implement conditional pull/no-op behavior | Core Git behavior |
| `src/Unlimotion/App.axaml.cs` | Invoke conditional pull before local storage switch | User flow integration |
| `src/Unlimotion.Test/BackupViaGitServiceTests.cs` | Add regression tests | Git behavior verification |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | Mandatory command/integration coverage | Verify call ordering, same-path skip and conflict stop |
| `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` | Add/update Settings UI test | Required UI-facing coverage via existing Avalonia.Headless pattern |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Folder without Git | Loads as local storage | Same behavior; no Git side effects |
| Existing Git folder | Loads local files first; remote pull is manual | Attempts remote pull before local storage load |
| Existing Git folder without remote | Manual pull can show remote error | Auto-pull no-ops and local storage still connects |
| Same folder reconnect | Reconnects current local storage | Reconnects current local storage without auto-pull |
| Pull conflict while changing folder | Potentially continues into normal storage load | Enters conflict resolver and defers normal storage reload |
| Git repo initialization | Only manual backup connect flow | Still only manual backup connect flow |
| UI flow | Browse path, then connect | Same visible flow |
| Tests | Existing backup/connect tests | Added regression for conditional pull and UI Settings flow |

## 18. Альтернативы и компромиссы
- Вариант: call existing `ConnectRepository(false)` when selected folder has no repo.
  - Плюсы: reuses backup onboarding.
  - Минусы: может clone/init/push, что прямо противоречит требованию "если гит репозитория там нет, ничего не делать".
  - Почему не выбран: слишком широкий side effect.
- Вариант: run pull immediately after folder picker selection.
  - Плюсы: ближе к буквальному "выбрать каталог".
  - Минусы: browse currently only edits setting; hidden network action before apply is surprising and misses manually typed path.
  - Почему не выбран: less consistent with current connect/apply semantics.
- Вариант: conditional pull inside `TaskStorageFactory.CreateFileStorage`.
  - Плюсы: centralized before every file storage creation.
  - Минусы: storage factory would depend on Git backup concerns and credentials.
  - Почему не выбран: weaker responsibility boundary.
- Выбранный вариант: service-level conditional pull called from local connect flow before storage switch.
  - Плюсы: bounded side effects, preserves current UX, testable, respects no-repo no-op.
  - Минусы: automatic pull happens only when user applies the selected path, not at browse time.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели дизайна и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, flow, API contract, conflict-stop, same-path skip, integration, data/state and rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | No-repo/no-remote no-op, no init/clone, background pull, conflict-stop and rollback covered. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, mandatory ordering test, targeted commands, UI coverage and file impact table есть. |
| E. Готовность к автономной реализации | 17-19 | PASS | Before/after, alternatives, plan and quality gate present; blocking questions absent. |
| F. Соответствие профилю | 20 | PASS | Соответствует `dotnet-desktop-client`, `ui-automation-testing`, `testing-dotnet`. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Outcome, no-op rule and Non-Goals are explicit. |
| 2. Понимание текущего состояния | 5 | Current Settings, App command, storage factory and Git service flow captured. |
| 3. Конкретность целевого дизайна | 5 | Service method, no-remote no-op, command integration, ordering, same-path skip and conflict-stop are specified. |
| 4. Безопасность (миграция, откат) | 5 | No persisted data changes; rollback is removal of conditional call/method. |
| 5. Тестируемость | 5 | Service, mandatory command/integration ordering, conflict-stop and UI tests are planned with commands. |
| 6. Готовность к автономной реализации | 5 | Ordered phases and no blocking questions. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: после первичного дизайна явно закреплены no-op для repo без remote, порядок pull before `SwitchStorage`, browse-only boundary, отсутствие init/clone и обязательная UI coverage. После дополнительного review исправлены conflict-stop before storage reload, противоречие valid repo/no remote, mandatory ordering coverage и same-path skip rule.
- Что осталось на решение пользователя: требуется только утверждение спеки фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS с validation limitation
- Что исправлено до завершения: auto-pull выбранного repo отделен от watcher текущего storage; добавлено command-level покрытие реального `ConnectCommand` и server-mode skip. Реализация сохраняет no-repo/no-remote no-op, выполняет conditional pull до `SwitchStorage`, обновляет Git/conflict metadata и останавливает normal load при conflict mode.
- Что проверено дополнительно: targeted Git service tests, watcher regression tests, App/local connection helper tests, real ConnectCommand tests, affected Settings UI test, test project build and desktop project build.
- Остаточные риски / follow-ups: полный TUnit run и старый `SettingsControl_SyncConflictResolutionMode_ShowsOpenResolverAction` завершаются кодом 1 без summary/report в текущем окружении; targeted tests for changed behavior pass.

## Approval
Получено: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершенный значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор central instruction stack и локального override | 0.96 | Нет | Осмотреть код Settings/Git flow | Нет | Нет | Задача проходит через delivery-task, `dotnet-desktop-client`, `ui-automation-testing`; code edits заблокированы до approval | `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `AGENTS.override.md` |
| SPEC | Анализ текущего Settings/storage/Git flow | 0.9 | Нет | Создать canonical spec | Нет | Нет | Найдены `ConnectCommand`, `BrowseTaskStoragePathCommand`, `TaskStorageFactory.SwitchStorage`, `BackupViaGitService.Pull()` and local repo selection helper | `src/Unlimotion/App.axaml.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs` |
| SPEC | Создание спецификации и quality gate | 0.93 | Нет | Ожидать `Спеку подтверждаю` перед EXEC | Да | Нет, approval еще не получен | Спека фиксирует bounded conditional pull before storage switch, no-repo no-op, tests and UI coverage | `specs/2026-05-12-auto-pull-selected-task-directory.md` |
| SPEC | Review fixes | 0.95 | Нет | Ожидать `Спеку подтверждаю` перед EXEC | Да | Да, пользователь попросил внести исправления review | Уточнены conflict-stop после auto-pull, no-remote no-op, обязательный ordering test и запуск auto-pull только при смене каталога | `specs/2026-05-12-auto-pull-selected-task-directory.md` |
| EXEC | Conditional pull implementation and tests | 0.86 | Нужен targeted test/build signal | Запустить targeted service/App-helper/UI tests | Нет | Да, пользователь подтвердил spec | Добавлен `PullExistingRepository`, local connect helper с same-path skip и conflict-stop, сервисные regression tests, App-helper ordering tests и Settings UI coverage; первый solution build timeout 120s без вывода | `src/Unlimotion.ViewModel/IRemoteBackupService.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` |
| EXEC | Validation and post-EXEC review | 0.9 | Нет для targeted scope; full-run blocked by runner/environment | Финальный отчет | Нет | Нет | Targeted service/helper/UI tests pass; test and desktop builds pass; full TUnit run and one pre-existing conflict UI test exit with code 1 without TUnit summary/report; post-EXEC review found no code changes required | `specs/2026-05-12-auto-pull-selected-task-directory.md`, `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/Unlimotion.Test-windows-net10.0-report.html` |
| EXEC | Review fixes implementation | 0.92 | Нет | Финальный отчет | Нет | Да, пользователь попросил внести review fixes | Исправлено использование watcher старого storage при pre-switch auto-pull; добавлены regression tests на watcher behavior, real `ConnectCommand` wiring и server-mode skip; targeted tests/builds passed | `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `specs/2026-05-12-auto-pull-selected-task-directory.md` |
