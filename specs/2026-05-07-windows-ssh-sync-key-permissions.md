# Windows SSH sync key permissions

## 0. Метаданные
- Тип (профиль): delivery-task; profiles: dotnet-desktop-client, ui-automation-testing; context: testing-dotnet
- Владелец: Unlimotion desktop / Git backup
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: на фазе SPEC менять только этот файл; на EXEC сохранить текущие настройки Git/SSH без миграции формата; обязательно добавить regression test и релевантный UI test; перед завершением запустить targeted tests, UI tests, build и полный тестовый прогон либо явно зафиксировать блокер.
- Связанные ссылки: скриншот пользователя с Windows OpenSSH error `WARNING: UNPROTECTED PRIVATE KEY FILE!` и ACL `CodexSandboxUsers` для `C:/Users/Kibnet/.ssh/id_ed25519`.

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
Исправить регрессию SSH-синхронизации на Windows: при выбранном приватном ключе приложение не должно запускать видимые OpenSSH-окна с ошибкой о слишком открытых правах и должно использовать выбранный ключ предсказуемо.

Outcome contract:
- Success means: SSH sync/pull/push/connect paths на Windows используют выбранный ключ без видимого OpenSSH окна и без ошибки `UNPROTECTED PRIVATE KEY FILE`; существующий или сгенерированный ключ на Windows приводится к ACL, приемлемому для OpenSSH; пользовательский UI flow выбора SSH-ключа остаётся работоспособным.
- Итоговый артефакт / output: кодовый фикс в `BackupViaGitService` и при необходимости `SettingsControl.axaml`/UI page object; regression tests в `src/Unlimotion.Test` и UI coverage в `tests/Unlimotion.UiTests.*` или Avalonia.Headless tests.
- Stop rules: остановиться после targeted + UI + build + full test evidence; если полный прогон объективно заблокирован окружением, зафиксировать точную команду, ошибку и next-best проверку.

## 2. Текущее состояние (AS-IS)
- Основной код Git backup живёт в `src/Unlimotion/Services/BackupViaGitService.cs`.
- Настройки SSH-ключа живут в `src/Unlimotion.ViewModel/SettingsViewModel.cs`: `SelectedSshPublicKeyPath` синхронизирует `GitSshPublicKeyPath` и `GitSshPrivateKeyPath`.
- UI настроек живёт в `src/Unlimotion/Views/SettingsControl.axaml`.
- Текущий HEAD уже содержит `SshPrivateKeyCredentials`, который передаёт private/public key в libgit2 через `git_credential_ssh_key_new`.
- В истории перед текущим HEAD был путь `git` CLI + `GIT_SSH_COMMAND`, который явно запускал `ssh -i "<key>" -o IdentitiesOnly=yes -o BatchMode=yes`. Такой путь на Windows проверяет ACL приватного ключа и даёт ошибку, как на скриншоте.
- `TrySetPrivateKeyPermissions` сейчас на Windows сразу возвращается и не чинит ACL ключа.
- После первого EXEC выяснено: ACL выбранного `id_ed25519_unlimotion` исправлен, системный `ssh -i` и `git ls-remote` с этим ключом успешно работают, но libgit2/libssh2 с тем же OpenSSH ed25519 ключом падает `could not read refs from remote repository`.
- В UI возможен рассинхрон: выбранный remote `github.com (SSH)`, а поле URL показывает `origin` HTTPS, потому что `GitRemoteUrl` не синхронизируется с выбранным remote, если уже заполнен.
- Existing tests:
  - `BackupViaGitServiceTests` покрывает credentials, connect/pull/push для file remotes и host key trust.
  - `SettingsViewModelTests` покрывает выбор SSH public key и готовность backup flow.
  - UI suites есть в `tests/Unlimotion.UiTests.Headless` / `tests/Unlimotion.UiTests.FlaUI`, но у backup controls в Settings UI почти нет automation ids.

## 3. Проблема
Корневая проблема: Windows SSH backup flow не нормализует транспорт и права приватного ключа как единый контракт. Сначала выбранный ключ попадал в системный OpenSSH с ACL, который OpenSSH считает слишком открытым. После ACL-fix оставшаяся причина - текущий libgit2/libssh2 transport не аутентифицируется тем же OpenSSH ed25519 ключом, который успешно работает через системный `ssh.exe`.

## 4. Цели дизайна
- Разделение ответственности: `BackupViaGitService` отвечает за Git/SSH transport и key file preparation; `SettingsViewModel` остаётся владельцем persisted settings; UI только отображает и вызывает команды.
- Повторное использование: один helper подготовки private key используется для configured SSH key во всех Git paths.
- Тестируемость: unit tests проверяют transport/key-preparation contract без реального GitHub; UI test проверяет пользовательский flow настроек с выбранным SSH key.
- Консистентность: сохранить текущую модель `SshPrivateKeyPath` / `SshPublicKeyPath`.
- Обратная совместимость: существующие ключи и настройки продолжают работать; формат config не меняется.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем формат persisted settings.
- Не добавляем новый SSH-agent flow и не требуем `ssh-add`.
- Не меняем UX экрана настроек за пределами automation ids/минимального состояния, нужного для теста.
- Не реализуем реальное сетевое e2e подключение к GitHub в тестах.
- Не меняем Android/iOS-specific native packaging.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Services/BackupViaGitService.cs` -> перед использованием configured private key валидирует путь, готовит ACL на Windows best-effort; на Windows для SSH remotes использует `git.exe` + `GIT_SSH_COMMAND` с выбранным ключом, на остальных путях сохраняет libgit2 transport.
- `src/Unlimotion.Test/BackupViaGitServiceTests.cs` -> regression coverage для Windows ACL helper / configured SSH key preparation и для отсутствия fallback к `DefaultCredentials` на SSH URL.
- `src/Unlimotion/Views/SettingsControl.axaml` -> stable automation ids для backup/SSH controls, если UI тесту не хватает селекторов.
- `tests/Unlimotion.UiTests.Authoring` + `tests/Unlimotion.UiTests.Headless` -> пользовательский сценарий: открыть Settings, включить backup, указать SSH remote, убедиться, что SSH-key controls и status для ключа доступны/работают.

### 6.2 Детальный дизайн
- Ввести/расширить private key preparation:
  - `GetConfiguredSshPrivateKeyPath` после проверки существования вызывает best-effort `TrySetPrivateKeyPermissions`.
  - На Windows helper выключает наследование и оставляет доступ текущему пользователю, `SYSTEM` и `Administrators`; избыточные ACE вроде `CodexSandboxUsers` удаляются.
  - На Unix helper сохраняет существующее поведение `0600`.
  - Ошибки ACL не должны ломать libgit2 path, но должны быть безопасно проглочены только в `Try*` helper; явное отсутствие файла всё ещё user-facing error.
- Transport:
  - Для Windows + SSH remotes основной путь для `fetch`/`push`/`ls-remote` становится `git.exe` с `GIT_SSH_COMMAND`, где указан выбранный private key, `IdentitiesOnly=yes`, `BatchMode=yes` и no-window process settings.
  - Для non-Windows SSH и HTTP(S) путей остаётся текущий libgit2 transport.
  - CLI path разрешён только после ACL hardening выбранного ключа, чтобы не возвращать `UNPROTECTED PRIVATE KEY FILE`.
- Output/evidence rules:
  - Unit evidence: тест показывает, что configured SSH credentials готовят private key; отдельный contract test фиксирует выбор Windows CLI transport для SSH remote.
  - Windows-only evidence: на Windows ACL helper удаляет явно добавленную лишнюю ACE с тестового файла. На non-Windows тест либо не выполняет ACL assertions, либо проверяет отсутствие ошибки.
  - UI evidence: headless UI scenario проходит через Settings SSH selection state без текстовых/позиционных селекторов.
- Обработка ошибок:
  - Missing key remains `SshPrivateKeyNotFound`.
  - ACL hardening failure не маскирует missing key и не создаёт новый file.
- Производительность: ACL check происходит только при SSH credential creation/generation; overhead малый относительно сетевой операции.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Для SSH URL и configured private key приложение должно использовать именно selected private key.
- Для Windows SSH remote selected private key должен попадать в `GIT_SSH_COMMAND`; системный default `id_ed25519` не должен использоваться неявно.
- Windows private key ACL после best-effort hardening не должен содержать arbitrary user/group ACE, если процесс имеет права их удалить.
- Public key selection rule остаётся прежним: `<path>.pub` -> private key `<path>`.

## 8. Точки интеграции и триггеры
- `GetCredentials(gitSettings)` -> при `IsSshUrl(url)` вызывает `GetConfiguredSshPrivateKeyPath`.
- Windows SSH `fetch`/`push`/`ls-remote` -> вызывает `RunGitCommandWithConfiguredSshKey`, который выставляет `GIT_SSH_COMMAND` и `GIT_TERMINAL_PROMPT=0`.
- `GenerateManagedRsaSshKey` -> после записи private key вызывает общий permission helper.
- Возможный `ssh-keygen` path в `GenerateSshKey` -> после успешной генерации также должен вызвать helper, чтобы ключи, созданные внешним `ssh-keygen`, были нормализованы.
- Settings UI test использует Settings tab и backup controls.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Новых settings migrations нет.
- Возможное изменение runtime state: private key file ACL на локальной машине пользователя.

## 10. Миграция / Rollout / Rollback
- Первый запуск после фикса: при следующей SSH операции выбранный private key будет best-effort нормализован.
- Обратная совместимость: если ACL нельзя изменить, libgit2 path всё равно может работать; пользователь получит прежние ошибки только если transport реально требует OpenSSH и ACL не исправлен.
- Rollback: вернуть permission helper к no-op и убрать связанные тесты; config не требует rollback.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - SSH URL + configured existing private key возвращает `SshPrivateKeyCredentials`, а не `DefaultCredentials`, для libgit2 credential path.
  - На Windows реальные SSH `fetch`/`push`/remote-head discovery используют `git.exe` + `GIT_SSH_COMMAND` с выбранным ключом.
  - На Windows helper удаляет лишнюю ACE с private key test file и оставляет доступ текущему пользователю.
  - `GenerateSshKey`/managed fallback вызывает permission helper после создания key pair.
  - Settings UI имеет stable automation coverage для SSH backup state.
  - Нет видимого `ssh.exe`/OpenSSH окна в sync/fetch/push/connect для выбранного SSH key.
  - При выборе remote `github.com (SSH)` поле URL синхронизируется с URL этого remote, если текущий URL был от другого known remote.
- Какие тесты добавить/изменить:
  - `BackupViaGitServiceTests`: reproducing test для Windows ACL с лишней ACE; credential/key-preparation regression; transport-selection regression.
  - `SettingsViewModelTests`: remote URL sync при переключении выбранного remote.
  - `MainWindowScenariosBase` или headless UI suite: открыть Settings, включить backup, указать SSH remote, проверить SSH section/status/buttons через automation ids.
- Characterization tests / contract checks:
  - Существующий `GetCredentials_ReturnsSshPrivateKeyCredentialsForConfiguredSshUrl` остаётся и расширяется permission evidence.
  - Существующий `BuildGitSshCommand_UsesExplicitKeyAndIdentitiesOnly` остаётся active helper check для Windows CLI transport.
- Базовые замеры до/после для performance tradeoff: не применимо, изменение не performance-sensitive.
- Команды для проверки:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/BackupViaGitServiceTests/*"`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/SettingsViewModelTests/*"` если затрагивается view-model behavior.
  - `dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*"` или более узкий UI test, если discovery позволит.
  - `dotnet build src/Unlimotion.sln`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`
- Stop rules для test/retrieval/tool/validation loops: если targeted test fails, фиксить до pass; full run failures анализировать, отличать regression от unrelated known flakes, но не завершать с failing targeted/UI tests.

## 12. Риски и edge cases
- ACL hardening может быть запрещён политикой Windows или правами пользователя. Смягчение: helper best-effort, отсутствие файла и auth failures остаются явными.
- Удаление лишних ACE может убрать ожидаемый доступ другого локального аккаунта к private key. Смягчение: private SSH key по смыслу должен быть доступен только владельцу/системным администраторам; это совпадает с OpenSSH requirements.
- Libgit2/libssh2 поддержка формата ключа может отличаться от системного OpenSSH. Смягчение: Windows SSH transport использует системный OpenSSH через `git.exe`, потому что он подтверждённо работает с выбранным ключом.
- UI test может потребовать добавить automation ids к controls, что является UI-facing изменением без визуальной смены. Смягчение: stable ids не меняют layout.

## 13. План выполнения
1. Добавить failing regression tests для Windows ACL preparation и SSH credentials path.
2. Если UI selectors отсутствуют, добавить automation ids к backup/SSH controls и headless UI test для Settings SSH state.
3. Реализовать Windows ACL hardening в `TrySetPrivateKeyPermissions` и вызвать helper после `ssh-keygen` generation.
4. Вернуть Windows SSH sync paths на `git.exe` + `GIT_SSH_COMMAND`, но только после ACL hardening и без видимых окон.
5. Запустить targeted unit/UI tests, затем build и полный доступный test run.
6. Выполнить post-EXEC review и зафиксировать результат.

## 14. Открытые вопросы
Нет блокирующих вопросов. Предлагаемое решение не меняет пользовательские настройки и не требует продуктового выбора.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`, `testing-dotnet`.
- Выполненные требования профиля:
  - План включает regression tests для поведения.
  - План включает UI test coverage для Settings SSH flow.
  - План включает `dotnet build`, targeted tests и full test run.
  - Platform-specific код ограничен service layer.
  - Stable automation ids используются вместо текстовых/позиционных селекторов.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Services/BackupViaGitService.cs` | Windows ACL hardening для private key; вызов helper после генерации и перед SSH credentials | Устранить OpenSSH `UNPROTECTED PRIVATE KEY FILE` и стабилизировать SSH sync |
| `src/Unlimotion.Test/BackupViaGitServiceTests.cs` | Regression tests для ACL/key preparation и SSH credentials | Воспроизвести и закрепить фикс |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | Синхронизация URL поля с выбранным remote, когда старый URL принадлежит другому known remote | Убрать рассинхрон `origin` HTTPS vs `github.com (SSH)` |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | Regression tests для выбора SSH remote и обновления URL | Закрепить UI-facing state без сетевого e2e |
| `src/Unlimotion/Views/SettingsControl.axaml` | Automation ids для backup/SSH controls, если нужны UI tests | Дать стабильные селекторы |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Page object properties для новых selectors | Использовать существующий UI test pattern |
| `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` | Headless scenario для Settings SSH state | Выполнить UI testing requirement |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Windows private key ACL | Helper no-op на Windows; existing key мог оставаться с лишней групповой ACE | Best-effort ACL hardening удаляет лишние ACE и оставляет owner/system/admin access |
| SSH transport | libgit2/libssh2 не аутентифицирует выбранный OpenSSH ed25519 key на Windows | Windows SSH fetch/push/ls-remote используют `git.exe` + `GIT_SSH_COMMAND` с выбранным ключом после ACL hardening |
| UI tests | Settings SSH flow не покрыт стабильными selectors | Headless UI scenario проверяет SSH backup state; ViewModel tests фиксируют remote URL sync |
| Config | `SshPrivateKeyPath` / `SshPublicKeyPath` | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: полностью вернуться к старому `DefaultCredentials`/ssh-agent.
- Плюсы: меньше кода ACL.
- Минусы: снова зависит от глобального agent state и не гарантирует selected key.
- Почему выбранное решение лучше в контексте этой задачи: selected key остаётся явным, а Windows ACL проблема устраняется у источника.

- Вариант: использовать только libgit2 и не трогать ACL.
- Плюсы: меньше вмешательство в user files.
- Минусы: не лечит уже выпущенный/возможный CLI path и не помогает пользователю с ключом, который OpenSSH явно отвергает.
- Почему выбранное решение лучше в контексте этой задачи: screenshot показывает именно ACL error; best-effort hardening делает ключ совместимым и с OpenSSH, и с текущим явным key flow.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, дизайн-цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, алгоритм ACL, ошибки и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Нет config migration; ACL side effect ограничен private key; план этапов задан. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, targeted/full commands и таблица файлов есть. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, альтернативы и quality gate заполнены; блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | .NET desktop, testing-dotnet и UI automation requirements отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна регрессия, явные Non-Goals и acceptance criteria. |
| 2. Понимание текущего состояния | 5 | Указаны service, ViewModel, UI, tests и исторический CLI path. |
| 3. Конкретность целевого дизайна | 5 | Описаны helper, ACL rules, integration points и error behavior. |
| 4. Безопасность (миграция, откат) | 5 | Config не меняется, rollback прост, ACL side effect описан. |
| 5. Тестируемость | 5 | Есть unit + UI tests и команды TUnit/MTP. |
| 6. Готовность к автономной реализации | 5 | План реалистичен, нет блокирующих вопросов. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: в spec добавлен явный вызов helper после `ssh-keygen` generation, чтобы покрыть ключи, созданные внешним OpenSSH, а не только managed fallback.
- Что осталось на решение пользователя: требуется только формальное утверждение спеки фразой ниже.

### Post-EXEC Review
- Статус: PASS
- Что проверено: Windows ACL hardening применяется к configured и generated private key; после дополнительной диагностики Windows SSH fetch/push/ls-remote переведены на `git.exe` + `GIT_SSH_COMMAND`; Settings SSH flow получил stable automation coverage.
- Что исправлено после проверки: UI scenario закреплён как `Settings_ssh_backup_flow_shows_key_controls_for_ssh_remote`; Windows ACL assertions проверяют отсутствие лишних explicit/inherited ACE; remote URL синхронизируется с выбранным known remote.
- Остаточные замечания: полный `Unlimotion.Test` дважды не дал чистый результат из-за unrelated/flaky ошибок вне Git/SSH (`TaskRelationsIndex` concurrent modification, затем `WantedFromUi_WhenConfirmed_ShouldUpdateDescendants` + timeout); targeted tests по изменённым областям и full headless UI прошли.

## Approval
Получено: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Диагностика регрессии Windows SSH sync | 0.86 | Нет live repro с реальным remote; есть screenshot и кодовый path | Создать и показать spec | Да | Да, требуется approval | Репозиторий требует SPEC-first для code changes; причина указывает на Windows OpenSSH ACL и выбранный SSH key transport | `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `tests/Unlimotion.UiTests.*`, `specs/2026-05-07-windows-ssh-sync-key-permissions.md` |
| SPEC | Draft + quality gate + post-SPEC review | 0.92 | Нет | Ждать фразу `Спеку подтверждаю` | Да | Да, ожидается решение пользователя | Spec содержит границы, test plan, UI testing requirement и rollout/rollback | `specs/2026-05-07-windows-ssh-sync-key-permissions.md` |
| EXEC | Regression unit tests + service fix | 0.88 | Нужен compile/test signal после зависших dotnet процессов | Добавить UI coverage и запустить проверки | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | Добавлены тесты для Windows ACL hardening; `BackupViaGitService` теперь harden-ит private key после генерации и перед SSH credentials | `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion/Services/BackupViaGitService.cs` |
| EXEC | Settings SSH UI coverage | 0.84 | Нужен headless UI test signal | Запустить targeted unit/UI/build/full tests | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | Добавлены stable automation ids и сценарий `Settings_ssh_backup_flow_shows_key_controls_for_ssh_remote`, чтобы закрепить UI-facing SSH backup state | `src/Unlimotion/Views/SettingsControl.axaml`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`, `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` |
| EXEC | Validation | 0.94 | Нет | Выполнить финальный sanity check | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | Пройдены targeted `BackupViaGitServiceTests` 19/19, targeted UI 3/3, full `Unlimotion.Test` 321/321, full headless UI 23/23 и `dotnet build src/Unlimotion.sln`; `git diff --check` без ошибок | `src/Unlimotion.sln`, `src/Unlimotion.Test/Unlimotion.Test.csproj`, `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj` |
| EXEC | Post-EXEC review | 0.95 | Нет | Сообщить результат пользователю | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | После ревью дополнительных кодовых проблем не найдено; финальный sanity check подтвердил отсутствие висящих тестовых/build процессов | Все изменённые файлы задачи |
| EXEC | Live diagnosis after remaining sync failure | 0.96 | Нет | Перевести Windows SSH transport на `git.exe` CLI path | Нет | Да, пользователь попросил `Исправляй` | Проверено: выбранный `id_ed25519_unlimotion` успешно аутентифицируется через системный `ssh`; `git ls-remote` по SSH remote работает; libgit2 с тем же ключом падает `could not read refs from remote repository` | `src/Unlimotion/Services/BackupViaGitService.cs`, локальный `C:\Projects\Education\Unlimotion Space\Tasks` только для read/fetch проверки |
| EXEC | Windows SSH CLI transport fix | 0.92 | Нет | Запустить targeted tests и UI tests | Нет | Да, пользователь попросил `Исправляй` | Windows SSH `fetch`/`push`/`ls-remote` теперь идут через `git.exe` с `GIT_SSH_COMMAND`; URL поля настроек синхронизируется с выбранным known remote | `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion.Test/BackupViaGitServiceTests.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs` |
| EXEC | Validation after CLI transport fix | 0.9 | Full unit run remains flaky outside changed area; full solution build hit timeout after desktop/test/android partial success | Выполнить финальный sanity check | Нет | Да, пользователь попросил `Исправляй` | Пройдены targeted Git backup 21/21, targeted SettingsViewModel 39/39, targeted UI 3/3, full headless UI 23/23, desktop build; exact real-repo SSH fetch command with selected key returned exit=0 | `src/Unlimotion.Test/Unlimotion.Test.csproj`, `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`, `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj` |
