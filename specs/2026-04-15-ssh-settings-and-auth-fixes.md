# Исправление SSH авторизации и выбора ключей в настройках Git backup

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client`
- Контексты: `testing-dotnet`
- Владелец: Unlimotion desktop Git backup
- Масштаб: `small`
- Целевой релиз / ветка: `codex/add-ssh-key-management-in-settings`
- Ограничения:
  - Не менять публичный конфиг Git для HTTP-авторизации.
  - Не делать общий редизайн экрана настроек.
  - Не менять существующий workflow push/pull для HTTP remote.
  - Не добавлять зависимости вне текущего стека `.NET` / `LibGit2Sharp` / штатных CLI (`git`, `ssh-keygen`), если без этого можно обойтись.
- Связанные ссылки:
  - `src/Unlimotion/Services/BackupViaGitService.cs`
  - `src/Unlimotion.ViewModel/SettingsViewModel.cs`
  - `src/Unlimotion/Views/SettingsControl.axaml`
  - `src/Unlimotion/App.axaml.cs`
  - `src/Unlimotion.ViewModel/IRemoteBackupService.cs`
  - `src/Unlimotion.ViewModel/TaskStorageSettings.cs`
  - `src/Unlimotion.Test/SettingsViewModelTests.cs`

## 1. Overview / Цель
Сделать SSH-настройки Git backup рабочими и предсказуемыми: генерация ключа должна создавать ключ в ожидаемой директории, обновлять список ключей и выбирать новый ключ, а операции clone/pull/push для SSH remote должны использовать выбранный приватный ключ надёжнее, чем текущий best-effort через `ssh-add`.

## 2. Текущее состояние (AS-IS)
- Экран настроек показывает список SSH public keys через вычисляемый getter `SshPublicKeys`, но не умеет уведомлять UI о его обновлении.
- Команда `RefreshSshKeysCommand` только повторно присваивает выбранный путь и не перечитывает содержимое директории `~/.ssh`.
- Команда генерации вызывает `ssh-keygen`, но после генерации не обновляет список ключей, поэтому новый `.pub` не появляется в `ComboBox`.
- `GenerateSshKey` принимает произвольную строку, включая путь, и фактически может создать ключ вне `~/.ssh`.
- Для SSH remote credential callback возвращает `DefaultCredentials` после попытки `ssh-add`, но не контролирует, что выбранный ключ действительно будет использован при fetch/push/clone.

## 3. Проблема
SSH flow в настройках частично реализован, но остаётся ненадёжным: пользователь не получает предсказуемый выбор/обновление ключа в UI, а выбранный ключ не гарантированно участвует в Git-операциях для SSH remote.

## 4. Цели дизайна
- Сделать генерацию ключа детерминированной и ограниченной директорией `~/.ssh`.
- Обеспечить немедленное обновление и выбор ключа в UI после генерации и по команде refresh.
- Сохранить совместимость с существующим HTTP flow.
- Минимизировать platform-specific код в `SettingsViewModel`.
- Добавить regression-тесты на логику выбора/обновления SSH ключей.

## 5. Non-Goals (чего НЕ делаем)
- Не внедряем полноценный менеджер `known_hosts`.
- Не меняем формат persisted-конфига вне уже добавленных `SshPrivateKeyPath` / `SshPublicKeyPath`.
- Не строим новый мастер настройки Git backup.
- Не переводим все Git-операции приложения на внешний `git` CLI без необходимости.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `BackupViaGitService`:
  - нормализует имя ключа до имени файла внутри `~/.ssh`;
  - генерирует ключ и валидирует результат;
  - предоставляет перечитывание public keys;
  - для SSH remote использует выбранный приватный ключ через более надёжный transport path, чем silent `ssh-add`.
- `SettingsViewModel`:
  - хранит наблюдаемый список `SshPublicKeys`;
  - умеет перечитывать ключи и синхронизировать выбранный ключ;
  - переводит выбор public key в связанный private key path.
- `App.axaml.cs`:
  - вызывает reload/select flow после refresh и после генерации;
  - не оставляет UI в устаревшем состоянии после успешной генерации.
- `SettingsControl.axaml`:
  - сохраняет текущий UI-контракт списка/кнопок без редизайна.
- `SettingsViewModelTests.cs`:
  - покрывает обновление списка и автоматический выбор public/private key pair.

### 6.2 Детальный дизайн
- `SettingsViewModel` будет переведён с вычисляемого getter `SshPublicKeys` на наблюдаемое свойство/состояние, которое можно явно обновлять через метод reload.
- Команда refresh будет вызывать reload списка ключей, а не только повторную установку `SelectedSshPublicKeyPath`.
- Команда генерации будет:
  - запускать генерацию ключа;
  - перечитывать список ключей;
  - выбирать сгенерированный `.pub`;
  - сохранять соответствующий `SshPrivateKeyPath`.
- `GenerateSshKey` будет принимать пользовательский ввод как имя файла, а не как путь:
  - пустое или whitespace значение -> `id_ed25519_unlimotion`;
  - path separators / absolute path -> отбрасываются через нормализацию до безопасного имени файла;
  - результатом всегда является путь внутри `~/.ssh`.
- Для SSH remote transport будет добавлен явный путь использования выбранного ключа:
  - при наличии `git` CLI и заданного `SshPrivateKeyPath` операции clone/fetch/push для SSH remote выполняются через `git` + `GIT_SSH_COMMAND` с `-i <privateKey>` и `IdentitiesOnly=yes`;
  - существующая LibGit2Sharp-ветка сохраняется для HTTP remote;
  - если SSH CLI path недоступен, пользователю показывается понятная ошибка вместо silent fallback.
- Обработка ошибок останется user-facing через toast, но без ложного ощущения успешной настройки.

## 7. Бизнес-правила / Алгоритмы (если есть)
- `SelectedSshPublicKeyPath = "<path>.pub"` всегда синхронно обновляет `GitSshPublicKeyPath` и `GitSshPrivateKeyPath = "<path>"`.
- Если после reload текущий выбранный public key существует в списке, выбор сохраняется.
- Если после генерации созданный public key существует, он становится активным выбранным ключом.
- SSH key generation допускает только имя файла, безопасное для размещения в `~/.ssh`.

## 8. Точки интеграции и триггеры
- `GenerateSshKeyCommand` -> `BackupViaGitService.GenerateSshKey(...)` -> reload списка -> выбор нового ключа.
- `RefreshSshKeysCommand` -> reload списка -> восстановление текущего выбора.
- `CloneOrUpdateRepo`, `Pull`, `Push`:
  - HTTP remote -> существующий путь через `LibGit2Sharp`;
  - SSH remote + выбранный ключ -> SSH transport path с выбранным ключом.

## 9. Изменения модели данных / состояния
- Persisted поля `Git:SshPrivateKeyPath` и `Git:SshPublicKeyPath` остаются без изменения формата.
- `SettingsViewModel` получает новое runtime-состояние для списка `SshPublicKeys`.
- Формат appsettings и bindable names не меняются.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется.
- При первом запуске после фикса существующие `SshPrivateKeyPath` / `SshPublicKeyPath` продолжают работать.
- Rollback:
  - вернуть вычисляемый getter списка ключей;
  - вернуть старый SSH credential path;
  - удалить новые regression-тесты.

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
1. После `Generate` новый ключ появляется в списке SSH public keys без перезапуска экрана.
2. После `Generate` новый ключ становится выбранным, а `GitSshPrivateKeyPath` синхронизируется с `.pub` без ручного выбора.
3. `Refresh` реально перечитывает директорию SSH ключей и не оставляет устаревший список.
4. Пользовательский ввод с путём вроде `..\foo\id_ed25519` не выводит генерацию за пределы `~/.ssh`.
5. Для SSH remote transport не зависит от silent `ssh-add`; выбранный ключ передаётся явно или пользователь получает понятную ошибку о недоступности нужного transport path.
6. HTTP remote flow и существующие настройки Git backup не деградируют.

### Какие тесты добавить/изменить
- `SettingsViewModelTests`:
  - reload списка SSH ключей обновляет bindable state;
  - выбор `.pub` обновляет приватный путь;
  - reload сохраняет выбранный ключ, если он всё ещё существует;
  - при генерации/выборе путь нормализуется ожидаемым образом через выделенную helper-логику, если она будет вынесена в тестируемый метод.
- При необходимости добавить service-level unit tests на нормализацию имени ключа и построение SSH CLI options.

### Команды для проверки
```powershell
dotnet build src/Unlimotion.sln
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj
dotnet test
```

## 12. Риски и edge cases
- На части окружений может не быть `git` CLI или `ssh-keygen`; в таком случае нужен явный user-facing error, а не silent ignore.
- Переход на SSH CLI path требует аккуратно сохранить существующую логику merge/status для локального репозитория.
- Нельзя блокировать UI-поток длительным внешним процессом без необходимости.
- На Windows и Unix нужно корректно экранировать путь к ключу в `GIT_SSH_COMMAND`.

## 13. План выполнения
1. Перевести список SSH ключей в наблюдаемое состояние `SettingsViewModel` и добавить reload/select helpers.
2. Исправить `App.axaml.cs`, чтобы refresh и generate вызывали реальное обновление списка и выбора.
3. Нормализовать генерацию имени ключа в `BackupViaGitService` и валидировать, что результат остаётся внутри `~/.ssh`.
4. Усилить SSH transport path для clone/pull/push с выбранным приватным ключом.
5. Добавить regression-тесты на ViewModel/helper-логику.
6. Прогнать targeted tests, затем `dotnet build` и `dotnet test`, затем выполнить post-EXEC review.

## 14. Открытые вопросы
Блокирующих вопросов нет.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - изменения локализованы в desktop UI + supporting service слоях Git backup;
  - UI flow обновления/выбора ключа будет покрыт regression-тестами на ViewModel/helper-логику;
  - перед завершением планируется `dotnet build` и `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | Наблюдаемый список SSH ключей, reload/select helpers | Исправить stale UI state и автоматический выбор |
| `src/Unlimotion/App.axaml.cs` | Обновление команд refresh/generate | Сразу обновлять список и selection |
| `src/Unlimotion/Services/BackupViaGitService.cs` | Нормализация имени ключа, SSH transport path, ошибки CLI | Сделать SSH flow надёжным |
| `src/Unlimotion.ViewModel/IRemoteBackupService.cs` | При необходимости уточнение контракта под reload/generate flow | Синхронизировать сервисный API с UI |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | Regression-тесты | Зафиксировать поведение и bugfix |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Refresh списка ключей | Не перечитывал директорию | Реально обновляет список и selection |
| Generate key | Генерировал ключ, но UI не обновлялся | Новый ключ появляется в списке и выбирается |
| Ввод имени ключа | Можно было передать путь | Используется безопасное имя файла внутри `~/.ssh` |
| SSH auth | Best-effort `ssh-add` + `DefaultCredentials` | Явный transport path с выбранным ключом или понятная ошибка |

## 18. Альтернативы и компромиссы
- Вариант: оставить `LibGit2Sharp` + `ssh-add`, но проверять exit code и улучшать сообщения.
- Плюсы:
  - меньше изменений в транспортном слое.
- Минусы:
  - не решает надёжно выбор конкретного ключа;
  - всё ещё зависит от внешнего `ssh-agent` и состояния окружения.
- Почему выбранное решение лучше в контексте этой задачи:
  - устраняет основной дефект ветки: выбранный ключ должен действительно использоваться;
  - делает поведение воспроизводимым и тестируемым.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, границы и non-goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Описаны ответственность, интеграция, данные и rollout. |
| C. Безопасность изменений | 11-13 | PASS | HTTP flow сохранён, migration не нужна, rollback понятен. |
| D. Проверяемость | 14-16 | PASS | Есть acceptance criteria, тест-план и команды проверки. |
| E. Готовность к автономной реализации | 17-19 | PASS | Масштаб малый, блокирующих вопросов нет, компромиссы описаны. |
| F. Соответствие профилю | 20 | PASS | Спека соответствует `dotnet-desktop-client` и `testing-dotnet`. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Задача ограничена SSH key management и transport path для Git backup. |
| 2. Понимание текущего состояния | 5 | Зафиксированы конкретные проблемы UI и credential flow по коду ветки. |
| 3. Конкретность целевого дизайна | 5 | Описаны reload/select flow, нормализация имени и transport strategy. |
| 4. Безопасность (миграция, откат) | 5 | Формат конфига не меняется, rollback локальный и прямой. |
| 5. Тестируемость | 5 | Есть конкретные regression-тесты и команды проверки. |
| 6. Готовность к автономной реализации | 5 | Блокирующих вопросов нет, объём контролируемый. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - Явно добавлен риск по отсутствию `git` / `ssh-keygen` в окружении.
  - Уточнено, что HTTP flow не должен деградировать.
  - Зафиксирован критерий о выборе нового ключа сразу после генерации.
- Что осталось на решение пользователя:
  - Ничего блокирующего.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Подготовка фикса SSH settings/auth после code review | 0.90 | Нужно финальное подтверждение пользователя по `QUEST` | Показать spec и дождаться фразы подтверждения | Да | Да, ожидается подтверждение спеки | Репозиторий требует `SPEC`-first и явный переход в EXEC только по фразе пользователя | `specs/2026-04-15-ssh-settings-and-auth-fixes.md` |
| EXEC | Реализация reload/select логики в settings UI | 0.93 | Нужна проверка совместимости с текущими тестами | Обновить `SettingsViewModel`, команды `App` и regression-тесты | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | Сначала исправлен пользовательский поток генерации/refresh, чтобы убрать stale UI state | `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs` |
| EXEC | Реализация безопасной генерации ключа и SSH transport path | 0.84 | Нужен compile/test сигнал на solution | Обновить `BackupViaGitService`, затем прогнать build/test | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | Для выбранного ключа нужен явный transport path; fallback через `ssh-add` убран как ненадёжный | `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion/AssemblyInfo.cs` |
| EXEC | Верификация и post-EXEC review | 0.95 | Нет блокирующих неизвестных, остались только внешние package warnings | Подготовить итоговый отчёт | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | Сборка решения и полный прогон `Unlimotion.Test` завершены успешно, критичных review-находок после фикса не осталось | `src/Unlimotion.sln`, `src/Unlimotion.Test/Unlimotion.Test.csproj`, `specs/2026-04-15-ssh-settings-and-auth-fixes.md` |
