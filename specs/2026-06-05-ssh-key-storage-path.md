# Выбор пути хранения SSH-ключей

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: пользователь / Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до фразы пользователя `Спеку подтверждаю` изменять только эту спецификацию; сохранить существующее поведение `~/.ssh`, если пользователь не выбрал другой каталог; UI-facing изменение должно сопровождаться UI-тестом.
- Связанные ссылки: Не применимо, внешних ссылок нет.

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
Добавить в настройки резервного копирования возможность выбрать каталог, где приложение ищет и создаёт SSH-ключи.

Outcome contract:
- Success means: пользователь видит путь каталога SSH-ключей в SSH-блоке настроек, может изменить его вручную или через выбор папки, список публичных ключей перечитывается из выбранного каталога, генерация ключа создаёт файлы в выбранном каталоге, а пустое значение сохраняет текущий default `~/.ssh`.
- Итоговый артефакт / output: изменения в ViewModel, UI, сервисе Git backup, локализации и тестах; отчёт с командами проверки.
- Stop rules: остановиться на SPEC до подтверждения; в EXEC остановиться при неоднозначном UX/API-выборе, невозможности запустить обязательные UI-тесты без объективной причины или при failing targeted tests.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion/Services/BackupViaGitService.cs` использует приватный `GetSshDirectory()`, который возвращает `%USERPROFILE%\.ssh` на Windows и `$HOME/.ssh` на остальных платформах.
- `GetSshPublicKeys()` читает `*.pub` только из этого default-каталога.
- `GenerateSshKey(string keyName)` создаёт private/public key pair только в этом default-каталоге.
- `GetKnownHostsPath()` также привязан к default `GetSshDirectory()`.
- `src/Unlimotion.ViewModel/SettingsViewModel.cs` хранит выбранные `GitSshPrivateKeyPath` и `GitSshPublicKeyPath`, но не хранит каталог для поиска/создания ключей.
- `src/Unlimotion/Views/SettingsControl.axaml` показывает SSH-блок с ComboBox активного ключа, кнопками refresh/copy и полем имени нового ключа, но без выбора папки.
- `src/Unlimotion/App.axaml.cs` уже использует `Dialogs.ShowOpenFolderDialogAsync(...)` для выбора папки локального хранилища задач; этот же механизм можно переиспользовать для SSH-каталога.
- UI-тесты уже проверяют SSH-блок через `SshKeysSection` и `SelectedSshPublicKeyComboBox` в `tests/Unlimotion.UiTests.Headless` и `tests/Unlimotion.UiTests.Authoring`.

## 3. Проблема
Приложение жёстко привязано к системному каталогу `~/.ssh`, поэтому пользователь не может хранить или генерировать SSH-ключи в отдельной папке, например в переносимом профиле, project-specific каталоге или другом безопасном location.

## 4. Цели дизайна
- Разделение ответственности: ViewModel хранит выбранный каталог и обновляет состояние, UI даёт ввод/выбор папки, `BackupViaGitService` разрешает effective SSH directory и выполняет файловые операции.
- Повторное использование: использовать существующий folder picker (`Dialogs.ShowOpenFolderDialogAsync`) и текущую санитизацию имени ключа.
- Тестируемость: добавить unit/contract tests на persistence/effective directory и UI test на наличие новых controls в SSH-блоке.
- Консистентность: новый row должен выглядеть как существующий `TaskStoragePath` field action row.
- Обратная совместимость: если новое поле отсутствует или пустое, поведение остаётся `~/.ssh`; существующие `SshPrivateKeyPath` и `SshPublicKeyPath` сохраняются.

## 5. Non-Goals (чего НЕ делаем)
- Не переносить уже существующие ключи между каталогами автоматически.
- Не менять формат private/public key pair и алгоритм `ssh-keygen`/managed fallback.
- Не вводить шифрование, keychain/credential manager или secret storage.
- Не менять remote URL/auth mode flow.
- Не менять публичный пользовательский контракт подключения Git backup, кроме нового выбора каталога ключей.
- Не добавлять массовый redesign Settings UI.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.ViewModel/TaskStorageSettings.cs` -> добавить `GitSettings.SshKeyStoragePath`.
- `src/Unlimotion.ViewModel/SettingsViewModel.cs` -> добавить persisted property `SshKeyStoragePath` и команду `BrowseSshKeyStoragePathCommand`; при изменении пути перечитывать ключи и сбрасывать selected key, если он не найден в новом списке.
- `src/Unlimotion.ViewModel/IRemoteBackupService.cs` -> предпочтительно сохранить текущий public interface без изменения; сервис будет читать каталог из `GitSettings`.
- `src/Unlimotion/Services/BackupViaGitService.cs` -> добавить effective directory resolver: configured non-empty `GitSettings.SshKeyStoragePath` -> full path, иначе default `GetSshDirectory()`. `GetSshPublicKeys()`, `GenerateSshKey(...)` и `GetKnownHostsPath()` должны использовать effective directory.
- `src/Unlimotion/App.axaml.cs` -> привязать `BrowseSshKeyStoragePathCommand` к `Dialogs.ShowOpenFolderDialogAsync(...)`, затем обновить `SshKeyStoragePath`, `ReloadSshPublicKeys()` и Git metadata.
- `src/Unlimotion/Views/SettingsControl.axaml` -> добавить field group "SSH keys folder" с TextBox и Browse button в `SshKeysSection`.
- `src/Unlimotion.ViewModel/Resources/Strings*.resx` -> добавить английские/русские строки для label и folder picker title.
- `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` -> добавить selectors новых controls.
- `tests/Unlimotion.UiTests.Headless/...` или `tests/Unlimotion.UiTests.Authoring/...` -> добавить/обновить UI coverage для SSH flow.
- `src/Unlimotion.Test/...` -> добавить unit tests для settings/service behavior.

### 6.2 Детальный дизайн
- Поток данных:
  1. При создании `SettingsViewModel` читается `Git:SshKeyStoragePath`.
  2. `ReloadSshPublicKeys()` вызывает `_backupService.GetSshPublicKeys()`, сервис читает актуальную configuration и возвращает `*.pub` из configured directory или default `~/.ssh`.
  3. Пользователь меняет TextBox или выбирает папку кнопкой `Browse`; ViewModel сохраняет новое значение в `Git:SshKeyStoragePath`.
  4. После выбора папки команда вызывает reload списка; если ранее выбранный public key не входит в новый список, `SelectedSshPublicKeyPath` становится `null`, а статус backup остаётся "Select an SSH key".
  5. `GenerateSshKeyCommand` создаёт ключ через сервис в effective directory и выбирает созданный public key.
- Контракты / API:
  - Новое persisted поле: `Git:SshKeyStoragePath`.
  - Пустой/null/whitespace `SshKeyStoragePath` означает "использовать default `~/.ssh`".
  - Direct text input допускает абсолютный или относительный путь; сервис нормализует через `Path.GetFullPath` для файловых операций.
- Output contract / evidence rules:
  - В отчёте EXEC указать targeted unit/UI tests, build и full test command или объективную причину, если full suite не выполнен.
- Visual planning artifact для UI-facing изменений:

```text
SSH keys
  hint text...

  Keys folder
  [ C:\Users\Me\.ssh                         ][Browse]
  Used folder: C:\Users\Me\.ssh

  Active key
  [ C:\Users\Me\.ssh\id_ed25519.pub          v]
  [Refresh] [Copy public key]

  New key
  [ id_ed25519_unlimotion                    ][Create]
```

- UI test video evidence для UI automation задач: `до` baseline video не применимо, потому что flow выбора каталога отсутствует. `после` video: попытаться только если существующий AppAutomation Headless/FlaUI runner или безопасный recorder предоставляет видео без расширения scope; fallback по умолчанию - headless UI test assertions + logs, так как в найденных test patterns video artifact не является встроенным обязательным output.
- Границы сохранения поведения: default `~/.ssh`, выбор активного public key и вывод private key path из `.pub` остаются.
- Обработка ошибок:
  - Если выбранный каталог не существует, `GetSshPublicKeys()` возвращает пустой список; `GenerateSshKey()` создаёт каталог, как сейчас создаёт default `.ssh`.
  - Если ключ с таким именем уже существует в выбранном каталоге, сохранить текущую ошибку `SshKeyAlreadyExists`.
  - Если configured directory невалиден для текущей ОС, ошибка возникает в сервисной операции и показывается через существующий toast path.
- Производительность: чтение `*.pub` остаётся top-directory-only; дополнительных watcher/subscription loops не вводить.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Effective SSH directory:
  - `configured = GitSettings.SshKeyStoragePath`
  - если `configured` null/empty/whitespace -> default `GetSshDirectory()`
  - иначе `Path.GetFullPath(configured)`
- `GetSshKeyPaths(effectiveDirectory, keyName)` продолжает предотвращать path traversal через `NormalizeSshKeyFileName` и `IsPathWithinDirectory`.
- После смены каталога выбранный ключ валиден только если его full path найден в новом списке `SshPublicKeys`.

## 8. Точки интеграции и триггеры
- `SettingsViewModel` constructor читает новое поле.
- Setter `SshKeyStoragePath` сохраняет configuration и refresh state.
- `BrowseSshKeyStoragePathCommand` открывает folder picker и вызывает reload.
- `RefreshSshKeysCommand` продолжает вызывать reload, но теперь из effective directory.
- `GenerateSshKeyCommand` работает с effective directory.
- `BackupViaGitService` known_hosts path использует effective directory для SSH CLI/libgit2 paths.

## 9. Изменения модели данных / состояния
- Новое persisted поле: `Git:SshKeyStoragePath` (`string?`).
- Calculated/effective state: если persisted field пустой, effective directory = default `~/.ssh`.
- Existing persisted fields `Git:SshPrivateKeyPath` и `Git:SshPublicKeyPath` не мигрируются и не удаляются.

## 10. Миграция / Rollout / Rollback
- Первый запуск после обновления: поле отсутствует, приложение продолжает использовать default `~/.ssh`.
- Rollout: безопасное добавление nullable/string setting; старые config-файлы читаются без миграции.
- Rollback: удаление/игнорирование `Git:SshKeyStoragePath` возвращает default behavior; созданные ключи в пользовательской папке не удаляются.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В SSH-блоке Settings появился field row выбора каталога ключей с `AutomationId` для TextBox и Browse button.
  - Под field row показывается фактическая папка, которая будет использована; при пустом поле это default `~/.ssh`.
  - Значение `SshKeyStoragePath` сохраняется в `Git:SshKeyStoragePath`.
  - При пустом `SshKeyStoragePath` сервис читает/создаёт ключи в default `~/.ssh`.
  - При непустом `SshKeyStoragePath` сервис читает `*.pub` и создаёт новые ключи в выбранном каталоге.
  - После смены каталога список public keys обновляется; selected key не остаётся pointing to old directory, если его нет в новом списке.
  - Existing selection behavior `id.pub` -> private key `id` сохраняется.
- Какие тесты добавить/изменить:
  - `SettingsViewModelTests`: persistence нового path; смена path вызывает reload/clears invalid selected key через fake backup service.
  - `SettingsViewModelTests`: effective path text показывает default directory при пустом поле и full path при пользовательском значении.
  - `BackupViaGitServiceTests`: custom configured SSH directory используется для `GetSshPublicKeys()`; key path generation остаётся within directory.
  - Headless/AppAutomation UI test: SSH flow exposes `SshKeyStoragePathTextBox`, `BrowseSshKeyStoragePathButton` and effective path text with expected automation ids.
- Characterization tests / contract checks для текущего поведения:
  - existing tests for `ReloadSshPublicKeys_*`, `SelectedSshPublicKeyPath_UpdatesPrivateKeyPath`, `NormalizeSshKeyFileName_*` must continue passing.
- Visual acceptance для UI-facing изменений:
  - New row appears inside `SshKeysSection` before "Active key".
  - Layout matches existing `FieldActionRow` pattern and does not remove current `SelectedSshPublicKeyComboBox`.
- UI video evidence:
  - Baseline: `Не применимо` - feature absent.
  - After: use existing UI test run as primary evidence; video fallback as described in section 6.2 if runner has no built-in safe recorder.
- Базовые замеры до/после для performance tradeoff: Не применимо, изменение не вводит hot path.
- Команды для проверки:
  - Targeted unit: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/SettingsViewModelTests/*|/*/*/BackupViaGitServiceTests/*"`
  - Targeted UI: `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release -- --treenode-filter "/*/*/SettingsRemoteTypeHeadlessTests/*"`
  - Build: `dotnet build src/Unlimotion.sln -c Release`
  - Full tests: `dotnet test src/Unlimotion.sln -c Release` или repo-proven full test workflow, если отличается после preflight.
- Stop rules для test/retrieval/tool/validation loops:
  - Не использовать VSTest `--filter` для TUnit/MTP проектов.
  - Если targeted test discovery показывает другой treenode path, скорректировать фильтр через `--list-tests`.
  - Если restore/build падает из-за NuGet/network/signature/auth environment issue, зафиксировать blocker и next-best local checks.

## 12. Риски и edge cases
- Relative path in config resolves differently depending on working directory. Mitigation: service uses `Path.GetFullPath`; UI displays stored value, tests cover effective behavior.
- Пользователь вводит каталог без доступа. Mitigation: list returns empty if directory absent; generation/connect errors go through existing error toast/status paths.
- Старый selected key остаётся выбранным после смены каталога. Mitigation: reload matches only current `SshPublicKeys`; tests cover clearing invalid selection.
- Изменение known_hosts path может surprise users who expect global known_hosts. Mitigation: use same effective directory as selected key storage; this keeps SSH artifacts together. Existing default remains unchanged.
- Interface churn in fake services/tests. Mitigation: keep `IRemoteBackupService` unchanged unless implementation evidence shows this is materially worse.

## 13. План выполнения
1. Добавить `GitSettings.SshKeyStoragePath`, ViewModel backing field/property/command и reload behavior.
2. Обновить `BackupViaGitService` effective SSH directory resolver and use sites.
3. Обновить Settings XAML, localization strings, Page Object selectors.
4. Добавить targeted unit/UI tests.
5. Запустить targeted tests, build, затем full test command или зафиксировать объективный blocker.
6. Выполнить post-EXEC review-loop и исправить findings.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`, `testing-dotnet`.
- Выполненные требования профиля:
  - План сохраняет UI-thread safety: файловые операции генерации остаются в существующем `Task.Run`.
  - План добавляет UI test coverage и сохраняет stable automation ids.
  - План включает `dotnet build`, targeted tests и full test command.
  - План фиксирует visual planning artifact и UI evidence fallback.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/TaskStorageSettings.cs` | Добавить `GitSettings.SshKeyStoragePath` | Persisted setting каталога SSH-ключей |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | Property/command/reload behavior | Управление выбранным каталогом из UI |
| `src/Unlimotion.ViewModel/SshKeyStoragePathResolver.cs` | Общий resolver effective SSH directory | UI-подсказка и сервис используют одну логику default/custom path |
| `src/Unlimotion.ViewModel/IRemoteBackupService.cs` | Скорее без изменений | Сохранить interface compatibility |
| `src/Unlimotion/Services/BackupViaGitService.cs` | Effective SSH directory resolver | Читать/создавать ключи в выбранном каталоге |
| `src/Unlimotion/App.axaml.cs` | Folder picker command wiring | Выбор папки через существующий dialog service |
| `src/Unlimotion/Views/SettingsControl.axaml` | Row для каталога SSH-ключей | UI-facing feature |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | EN strings | Локализация label/dialog |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | RU strings | Локализация label/dialog |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Selectors новых controls | Stable UI automation |
| `tests/Unlimotion.UiTests.Headless/Tests/SettingsRemoteTypeHeadlessTests.cs` | UI assertion нового row | Required UI coverage |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | Unit tests ViewModel behavior | Persistence/reload contract |
| `src/Unlimotion.Test/BackupViaGitServiceTests.cs` | Service tests | Effective directory contract |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Каталог поиска ключей | Только default `~/.ssh` | `Git:SshKeyStoragePath` или default `~/.ssh` |
| Генерация ключа | Только default `~/.ssh` | Выбранный каталог или default |
| Settings UI | Нет выбора каталога | TextBox + Browse button в SSH-блоке |
| Existing selected key | Absolute public/private key paths | Без изменений |
| No configured path | Default `~/.ssh` | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: изменить `IRemoteBackupService.GetSshPublicKeys(path)` и `GenerateSshKey(keyName, path)`.
- Плюсы: явный параметр, проще читать в call site.
- Минусы: больше churn по fake services/tests, public interface changes, риск рассинхронизации с Git configuration.
- Почему выбранное решение лучше в контексте этой задачи: настройка уже относится к `GitSettings`, сервис уже читает Git config для операций backup, поэтому persisted config даёт меньше API-churn и сохраняет совместимость.

- Вариант: выбирать конкретный private key file вместо каталога.
- Плюсы: точнее для подключения.
- Минусы: не решает генерацию новых ключей в выбранном месте и не соответствует формулировке "путь хранения".
- Почему выбранное решение лучше: каталог покрывает и поиск, и генерацию, а существующий ComboBox продолжает выбирать активный public key.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals заполнены. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, алгоритм, state, rollout и ошибки описаны. |
| C. Безопасность изменений | 11-13 | PASS | Миграция, rollback, edge cases и границы совместимости зафиксированы. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, unit/UI tests и команды проверки указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, alternatives и отсутствие блокирующих вопросов зафиксированы. |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client`, `ui-automation-testing`, `testing-dotnet` отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Outcome и Non-Goals проверяемы. |
| 2. Понимание текущего состояния | 5 | Указаны конкретные файлы и текущая привязка к `~/.ssh`. |
| 3. Конкретность целевого дизайна | 5 | Есть data flow, persisted field, UI wireframe и service resolver. |
| 4. Безопасность (миграция, откат) | 5 | Default behavior сохраняется, rollback описан. |
| 5. Тестируемость | 5 | Указаны unit, service и UI tests plus commands. |
| 6. Готовность к автономной реализации | 5 | Нет открытых вопросов, план и file table достаточны. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-05-ssh-key-storage-path.md`; instruction stack: `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, local `AGENTS.override.md`; selected profile: `dotnet-desktop-client` + `ui-automation-testing`; open questions: none; planned changed files listed in section 16.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: проверены найденные SSH call sites in `BackupViaGitService`, Settings bindings in `SettingsViewModel`/`SettingsControl.axaml`, existing folder picker, existing UI Page Object and Headless SSH test.
  - Contract pass: spec keeps default `~/.ssh`, adds persisted path, includes UI test requirement, and does not include automatic key migration or broader backup redesign.
  - Adversarial risk pass: challenged old selected key after directory switch, relative path ambiguity, inaccessible directory, interface churn, known_hosts consistency and video evidence requirement.
  - Re-review after fixes / Fix and re-review: not needed; no blocking findings found after initial draft.
  - Stop decision: PASS because scope, acceptance, validation and profile requirements are concrete and no human product choice remains.
- Evidence inspected: `BackupViaGitService.GetSshPublicKeys`, `GenerateSshKey`, `GetSshDirectory`, `GetKnownHostsPath`; `SettingsViewModel.SelectedSshPublicKeyPath`, `ReloadSshPublicKeys`; `SettingsControl.axaml` SSH section; `Dialogs.ShowOpenFolderDialogAsync`; `SettingsRemoteTypeHeadlessTests`; `MainWindowPage`.
- Depth checklist:
  - Scope drift / unrelated changes: spec limits work to SSH key storage path.
  - Acceptance criteria: concrete and testable.
  - Validation evidence: planned targeted unit, targeted UI, build, full tests.
  - Unsupported claims: all AS-IS claims are tied to inspected files.
  - Regression / edge case: old selection, default path, missing directory, relative path covered.
  - Comments/docs/changelog: no docs/changelog required unless implementation reveals user-facing release notes practice.
  - Hidden contract change: `IRemoteBackupService` kept unchanged by default; existing key path fields preserved.
  - Manual-review challenge: likely challenge is whether known_hosts should follow selected directory; spec states this explicitly as chosen behavior with default compatibility.
- No-findings justification: spec contains a concrete UI artifact, acceptance tests and conservative compatibility path.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | UI video recorder capability is not confirmed during SPEC | During EXEC, attempt only if runner/recorder support is present; otherwise report fallback evidence | follow-up |

- Fixed before continuing: none.
- Checks rerun: SPEC linter and rubric self-check completed in this section.
- Needs human: yes, approval phrase `Спеку подтверждаю` is required to enter EXEC.
- Residual risks / follow-ups: video evidence likely fallback; full solution test command may need adjustment after TUnit discovery.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec; `git status --short`; `git diff --stat`; diffs for `SettingsViewModel`, `BackupViaGitService`, `App.axaml.cs`, Settings XAML/resources, Page Object, headless UI test, unit tests; validation evidence below; docs/changelog impact: no docs/changelog update required by current repo pattern for this scoped UI setting.
- Decision: можно завершать; full solution build blocker is isolated to Android AOT, while relevant desktop build and full runnable test projects passed.
- Review passes:
  - Scope/Evidence pass: inspected all changed code/test files in diff, validation outputs, and current status. One unrelated untracked spec exists: `specs/2026-06-07-conflict-resolver-mobile-scroll.md`; not part of this task and not modified.
  - Contract pass: implementation adds `Git:SshKeyStoragePath`, keeps default `~/.ssh`, preserves `IRemoteBackupService`, updates SSH settings UI with stable automation ids, adds ViewModel/service/headless coverage, and does not migrate existing keys or redesign backup flow.
  - Adversarial risk pass: challenged stale private key after clearing selected public key, generation path not covered by initial tests, known_hosts using old default path, relative/blank configured path behavior, UI selector stability, and full validation gaps.
  - Re-review after fixes / Fix and re-review: added `GenerateSshKey_CreatesKeyPairInConfiguredSshKeyStoragePath`; reran `BackupViaGitServiceTests` and full `Unlimotion.Test` after the fix.
  - Stop decision: PASS; remaining risks are environment/runner limitations, not unaddressed product behavior.
- Evidence inspected:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -- --treenode-filter "/*/*/SettingsViewModelTests/*"` -> passed 64/64.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -- --treenode-filter "/*/*/BackupViaGitServiceTests/*"` -> passed 51/51 after review fix.
  - `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release --no-restore -- --treenode-filter "/*/*/SettingsRemoteTypeHeadlessTests/*"` -> passed 2/2.
  - `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release --no-restore` -> passed.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore` -> passed 435/435 after review fix.
  - `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release --no-restore` -> passed 28/28.
  - `dotnet build src/Unlimotion.sln -c Release --no-restore` -> failed in unrelated `src/Unlimotion.Android` AOT precompile: `Unable to open file 'obj\Release\net10.0-android\android-arm64\Mono.Android.dll.tmp\temp.s': Invalid argument`.
- Depth checklist:
  - Scope drift / unrelated changes: changed files match approved file table; unrelated untracked spec is explicitly excluded.
  - Acceptance criteria: field row/automation ids, persisted config, default/custom directory, generation in configured directory, selection clearing and private-key clearing are covered.
  - Validation evidence: targeted and full unit/headless tests passed; desktop build passed; solution build blocked by Android AOT only.
  - Unsupported claims: current behavior and implementation claims tied to inspected diff and command outputs.
  - Regression / edge case: blank path, relative path normalization, no selected stale key, generated key pair path, known_hosts effective path covered by code review/tests where practical.
  - Comments/docs/changelog: no new comments; no stale docs changed; changelog not in scope.
  - Hidden contract change: `IRemoteBackupService` unchanged; new `GitSettings` field is additive; clearing private key when public selection is cleared is intentional consistency fix.
  - Manual-review challenge: likely challenge is video evidence and full solution build; both are documented with objective fallback/blocker.
- No-findings justification: after review fix, all acceptance criteria have code/test evidence or an explicit environment fallback.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | tests | Initial service coverage did not directly prove `GenerateSshKey` writes into configured SSH directory | Add generation regression test and rerun affected/full unit checks | fixed |
| LOW | evidence | Existing headless/AppAutomation workflow did not produce video evidence | Use headless UI assertions/logs as next-best evidence and report fallback | accepted-risk |
| LOW | validation | Full solution build fails in Android AOT, outside changed desktop/settings surface | Run relevant desktop build and full runnable test projects; report Android blocker | accepted-risk |

- Fixed before final report: added `GenerateSshKey_CreatesKeyPairInConfiguredSshKeyStoragePath`.
- Checks rerun: `BackupViaGitServiceTests` targeted 51/51; full `Unlimotion.Test` 435/435.
- Validation evidence: listed above.
- Unrelated changes: `?? specs/2026-06-07-conflict-resolver-mobile-scroll.md` existed in status and is unrelated/not touched.
- Needs human: no.
- Residual risks / follow-ups: Android solution build AOT blocker remains outside this change; video evidence remains fallback because the existing headless runner did not emit video artifacts.

### Post-EXEC Review Addendum: branch review fixes
- Статус: PASS для исправлений по review findings.
- Scope reviewed: `SettingsViewModel.ReloadSshPublicKeys`, `LanguageOptionsVersion` notifications, `SshKeyStorageEffectivePathText`, regression tests in `SettingsViewModelTests`.
- Fixed findings:
  - Invalid/intermediate SSH key storage path no longer crashes settings reload; path-list reload treats path/input exceptions as an empty key list while display fallback still shows the raw invalid input.
  - Localized effective-path hint now raises `PropertyChanged` during localization refresh.
- Evidence inspected:
  - Before fix: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -- --treenode-filter "/*/*/SettingsViewModelTests/*"` failed on `SshKeyStoragePath_DoesNotThrowForInvalidIntermediateInput` and `LanguageMode_UpdatesSshKeyStorageEffectivePathText`, reproducing both review findings. One unrelated `SwitchRemoteConnectionTypeCommand_UpdatesSelectedRemoteFromServiceResult` failure appeared in that broad class run and did not reproduce after the fix.
  - After fix: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -- --treenode-filter "/*/*/SettingsViewModelTests/SshKeyStoragePath_DoesNotThrowForInvalidIntermediateInput"` -> passed 1/1.
  - After fix: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -- --treenode-filter "/*/*/SettingsViewModelTests/LanguageMode_UpdatesSshKeyStorageEffectivePathText"` -> passed 1/1.
  - After fix: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -- --treenode-filter "/*/*/SettingsViewModelTests/*"` -> passed 68/68.
  - After fix: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -- --treenode-filter "/*/*/BackupViaGitServiceTests/*"` -> passed 51/51.
  - After fix: `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release --no-restore -- --treenode-filter "/*/*/SettingsRemoteTypeHeadlessTests/*"` -> passed 2/2.
  - After fix: `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release --no-restore` -> passed 28/28.
  - After fix: `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release --no-restore` -> passed.
  - Full `Unlimotion.Test` was attempted three times and remains unstable outside the changed surface: first run failed 1/439 on `SettingsControlResponsiveUiTests.SettingsControl_TaskTreeExpansionStateCheckBox_PersistsSetting`, second run failed 1/439 on `MainWindowViewModelTests.PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`, third run with `--maximum-parallel-tests 1 --timeout 300s` failed 2/439 on `MainControlTaskCardLayoutUiTests.CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls` and `MainWindowViewModelTests.CopyTaskOutline_UsesMarkdownAndDescriptionSettings`. Each failed test passed when rerun individually.
- Residual risks / follow-ups: full `Unlimotion.Test` has pre-existing or environment-sensitive flakiness in unrelated UI/outline tests; targeted SSH settings/service tests, headless UI suite and desktop build pass.

### Post-EXEC Review Addendum: GitHub reviewer comment
- Статус: PASS для reviewer feedback thread `PRRT_kwDOGtM4f86Hst7X`.
- Reviewer finding: Windows SSH CLI transport bypassed the libgit2 certificate callback path, so OpenSSH still used the default profile `~/.ssh/known_hosts` even when `Git:SshKeyStoragePath` was configured.
- Fix: `RunGitCommandWithConfiguredSshKey` now computes the effective `known_hosts_unlimotion` path from current `GitSettings`, ensures the parent directory exists, and passes it to `GIT_SSH_COMMAND` through `-o UserKnownHostsFile=...`.
- Regression coverage: `BuildGitSshCommand_UsesExplicitKeyAndConfiguredKnownHostsFile` verifies the configured SSH storage folder is reflected in the generated CLI command.
- Evidence inspected:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore -- --treenode-filter "/*/*/BackupViaGitServiceTests/*"` -> passed 52/52.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Context discovery | 0.88 | Нет для SPEC | Создать рабочую спецификацию | Нет | Нет | Найдены SSH service/UI/test call sites и выбран conservative design с default fallback | `BackupViaGitService.cs`, `SettingsViewModel.cs`, `SettingsControl.axaml`, UI tests |
| SPEC | Spec draft and review | 0.93 | Подтверждение пользователя | Запросить `Спеку подтверждаю` | Да | Да, требуется approval gate перед EXEC | Quest-mode запрещает менять код до утверждения спеки; spec прошла self-check/review | `specs/2026-06-05-ssh-key-storage-path.md` |
| EXEC | Approval received | 0.94 | Нет | Начать реализацию в границах спеки | Нет | Да, пользователь написал `Спеку подтверждаю` | Approval gate пройден, можно менять код и тесты по утверждённому плану | `specs/2026-06-05-ssh-key-storage-path.md` |
| EXEC | Core setting and service changes | 0.86 | Build/test feedback | Обновить UI, локализацию и selectors | Нет | Нет | Добавлены `GitSettings.SshKeyStoragePath`, property/command wiring and effective SSH directory resolver without changing `IRemoteBackupService` | `TaskStorageSettings.cs`, `SettingsViewModel.cs`, `App.axaml.cs`, `BackupViaGitService.cs` |
| EXEC | Settings UI and localization | 0.87 | Test feedback | Добавить unit/UI coverage | Нет | Нет | SSH section now exposes folder TextBox/Browse controls with localized labels and stable automation ids | `SettingsControl.axaml`, `Strings.resx`, `Strings.ru.resx`, `MainWindowPage.cs` |
| EXEC | Test coverage added | 0.84 | Validation results | Запустить targeted unit/UI проверки | Нет | Нет | Added ViewModel/service regression tests and headless UI selector assertions for the new SSH key folder controls | `SettingsViewModelTests.cs`, `BackupViaGitServiceTests.cs`, `SettingsRemoteTypeHeadlessTests.cs` |
| EXEC | Validation run | 0.82 | Post-EXEC review | Выполнить review diff/status | Нет | Нет | Targeted unit/service/headless UI and full unit/headless suites passed; solution build has unrelated Android AOT blocker, desktop build passed | test/build commands |
| EXEC | Visual evidence capture | 0.86 | Нет | Передать GIF artifacts пользователю | Нет | Да, пользователь запросил GIF для desktop и Android | Captured desktop app screenshots and Android emulator screenshots, then assembled GIFs showing empty/default and custom SSH key folder states | `artifacts/ssh-key-storage-path-demo-9/desktop-ssh-key-storage-path.gif`, `artifacts/ssh-key-storage-path-demo-9/android/android-ssh-key-storage-path.gif` |
| EXEC | UX clarification | 0.89 | Нет | Финальный diff/whitespace review | Нет | Да, пользователь уточнил, что пустое поле должно показывать фактический каталог | Added effective SSH key folder text under the field, shared resolver, localization, unit tests and headless selector coverage | `SshKeyStoragePathResolver.cs`, `SettingsViewModel.cs`, `SettingsControl.axaml`, tests |
| EXEC | Branch review fixes | 0.89 | Нет | Финальный diff/status и при необходимости commit/push | Нет | Да, пользователь попросил исправить review findings | Added regression coverage and fixed invalid path reload safety plus localized effective-path notification; unrelated full-unit flakes passed in isolated reruns | `SettingsViewModel.cs`, `SettingsViewModelTests.cs`, `specs/2026-06-05-ssh-key-storage-path.md` |
| EXEC | GitHub reviewer feedback | 0.91 | Нет | Проверить, закоммитить и запушить | Нет | Да, пользователь сообщил о комментарии ревьювера на GitHub | Addressed the P2 reviewer thread by routing Windows Git CLI SSH host trust to the configured `known_hosts_unlimotion` path | `BackupViaGitService.cs`, `BackupViaGitServiceTests.cs`, `specs/2026-06-05-ssh-key-storage-path.md` |
