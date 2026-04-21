# Доработки рестайлинга настроек Git и хранилища

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`; контекст `testing-dotnet`
- Владелец: Codex
- Масштаб: medium
- Целевой релиз / ветка: текущая ветка рестайлинга настроек
- Ограничения: менять только поведение и разметку экрана настроек, связанные ViewModel/service contracts и regression-тесты; не менять модель конфигурации без необходимости
- Связанные ссылки: замечания пользователя от 2026-04-21

## 1. Overview / Цель
Исправить восемь UX/behavior замечаний к текущему экрану настроек: показывать фактический полный путь для папки данных, дать ручное обновление Git-метаданных, автозаполнять репозиторий из выбранного remote, упростить основные Git-поля, явно указать единицы интервалов и разрешать sync/pull/push по remote + канонической ветке отправки.

## 2. Текущее состояние (AS-IS)
- Экран настроек живёт в `src/Unlimotion/Views/SettingsControl.axaml`.
- Состояние экрана и derived flags живут в `src/Unlimotion.ViewModel/SettingsViewModel.cs`.
- Git-метаданные читает `IRemoteBackupService`, реализация для libgit2 находится в `src/Unlimotion/Services/BackupViaGitService.cs`.
- Команды экрана настраиваются в `src/Unlimotion/App.axaml.cs`.
- Regression-тесты настроек находятся в `src/Unlimotion.Test/SettingsViewModelTests.cs`.
- Сейчас tooltip поля "Папка с данными" показывает исходную строку `TaskStoragePath`, поэтому относительный путь остаётся относительным.
- `ReloadGitMetadata()` уже перечитывает remotes/ref/auth type, но отдельной кнопки обновления Git-метаданных в основном блоке нет.
- `IRemoteBackupService` отдаёт имена remote и auth type, но не отдаёт URL remote, поэтому ViewModel не может заполнить `GitRemoteUrl` из выбранного источника.
- В основной секции есть поля "Репозиторий", "Ветка", "Способ входа"; "Удаленный источник" виден только при нескольких remotes, а "Ветка отправки" находится в расширенных настройках.
- Ручные действия "Получить изменения сейчас" и "Отправить изменения сейчас" сейчас находятся в расширенном блоке вне `GitBackupEnabled`, хотя относятся к Git backup repository.
- `CanSyncRepository` сейчас зависит от `GitRemoteUrl` и состояния `Connected`, поэтому sync/pull/push недоступны, если есть выбранный remote и push ref, но нет заполненного URL или статус ещё не Connected.
- Интервалы pull/push подписаны без единиц измерения.

## 3. Проблема
Основной блок Git-настроек показывает вторичные или неинформативные поля, а готовность действий синхронизации вычисляется не из реально необходимых параметров выбранного remote и ветки отправки.

## 4. Цели дизайна
- Разделение ответственности: ViewModel рассчитывает derived state и синхронизирует выбранный remote с URL; сервис только читает repository metadata.
- Повторное использование: существующий `ReloadGitMetadata()` остаётся единой точкой обновления remote/ref списков.
- Тестируемость: новые правила readiness и автозаполнения покрываются unit-тестами `SettingsViewModel`.
- Консистентность: основные поля соответствуют тому, что нужно пользователю для синхронизации: репозиторий, remote, ветка отправки.
- Обратная совместимость: существующие настройки `Git.RemoteUrl`, `Git.Branch`, `Git.RemoteName`, `Git.PushRefSpec` сохраняются; удаление persisted fields не выполняется.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем формат конфигурации и имена существующих Git-настроек.
- Не меняем алгоритмы `Pull`, `Push`, `CloneOrUpdateRepo`, расписание Quartz и storage connect flow.
- Не добавляем новые Git-операции помимо перечитывания метаданных уже существующим сервисом.
- Не меняем внешний вид всего экрана настроек за пределами перечисленных замечаний.
- Не вводим полноценную миграцию старого `Git.Branch` в `Git.PushRefSpec`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `SettingsViewModel.cs` -> добавить full-path tooltip property для папки данных; добавить командный слот `RefreshGitMetadataCommand`; синхронизировать `GitRemoteUrl` из выбранного remote, если URL пустой; обновить правило `CanSyncRepository`.
- `IRemoteBackupService.cs` -> добавить метод чтения URL remote по имени.
- `BackupViaGitService.cs` -> реализовать чтение URL remote из libgit2 repository.
- `SettingsControl.axaml` -> переставить Git-поля: в основной Git backup блок вывести всегда видимый "Удаленный источник" и "Ветка отправки", убрать самостоятельное поле "Ветка" и readonly "Способ входа"; добавить кнопку обновления рядом с Git-настройками; добавить единицы "сек." к интервалам; перенести pull/push действия внутрь `GitBackupEnabled` блока.
- `App.axaml.cs` -> назначить `RefreshGitMetadataCommand`, вызывающую `ReloadGitMetadata()`.
- `SettingsViewModelTests.cs` -> добавить regression-тесты новых derived rules.

### 6.2 Детальный дизайн
- `TaskStoragePathTooltip` возвращает полный фактический путь, который будет использовать storage flow для текущего значения поля. Для непустого `TaskStoragePath` это нормализованный `Path.GetFullPath(TaskStoragePath)`. Для пустого поля tooltip должен использовать тот же fallback, что подключение локального storage: effective default path, а если он пустой, fallback `"Tasks"`, затем `Path.GetFullPath`.
- Вычисление tooltip должно быть safe: некорректный промежуточный ввод в `TextBox` не должен бросать исключение из getter; при ошибке разрешения пути tooltip возвращает исходное значение или последний безопасно вычисленный fallback.
- При изменении `TaskStoragePath` уведомление PropertyChanged должно затронуть tooltip property. Если Fody не отследит computed dependency автоматически, добавить явный helper/сеттер-структуру без ручного INotify boilerplate сверх нужного.
- `IRemoteBackupService.GetRemoteUrl(string remoteName)` возвращает URL remote или `null`.
- `ReloadGitMetadata()` после загрузки remotes/refs вызывает helper автозаполнения:
  - если `GitRemoteUrl` уже заполнен, не перезаписывать его;
  - если `GitRemoteName` выбран и service вернул URL, сохранить его в `GitRemoteUrl`;
  - если выбранного remote нет, но remote ровно один, выбрать его и заполнить URL;
  - если remote несколько и выбранного нет, URL не угадывать.
- Сеттер `GitRemoteNameDisplay` после изменения имени remote вызывает тот же helper, чтобы ручной выбор remote мог заполнить пустой repository URL.
- `CanSyncRepository` становится true, когда backup включён, нет busy-состояния, заполнены `GitRemoteName` и `GitPushRefSpec`. Это правило применяется к "Синхронизировать сейчас", "Получить изменения сейчас" и "Отправить изменения сейчас".
- `CanConnectRepository` остаётся завязанным на clone/connect credentials и `GitRemoteUrl`, чтобы подключение нового репозитория не ломалось.
- "Удаленный источник" всегда виден в основном Git backup блоке, даже если remote один. При одном remote ViewModel может выбрать его автоматически; UI не должен скрывать поле по `HasMultipleRemotes`.
- "Ветка отправки" показывает и сохраняет каноническое имя ref как сейчас использует `PushRefSpec`, например `refs/heads/main`. Список `Refs` отдаёт canonical refs, и UI показывает их без преобразования в короткое имя.
- Если `GitPushRefSpec` пустой, а старое `GitBranch` заполнено, ViewModel инициализирует `GitPushRefSpec` из `GitBranch`: canonical значение сохраняется как `refs/heads/{GitBranch}`, кроме случая, когда `GitBranch` уже начинается с `refs/`.
- "Ветка" (`GitBranch`) остаётся в ViewModel/конфиге для clone checkout compatibility, но не показывается как самостоятельное поле в основной форме.
- Auth mode остаётся вычисляемым внутренним состоянием для показа token/SSH credential blocks, но readonly поле "Способ входа" убирается из основного UI.

## 7. Бизнес-правила / Алгоритмы
| Сценарий | Условие | Поведение |
| --- | --- | --- |
| Tooltip папки данных | `TaskStoragePath = "data/tasks"` | Tooltip показывает `Path.GetFullPath("data/tasks")` |
| Tooltip папки данных | `TaskStoragePath` абсолютный | Tooltip показывает нормализованный абсолютный путь |
| Tooltip папки данных | `TaskStoragePath` пустой | Tooltip показывает полный путь effective fallback storage path, минимум `Path.GetFullPath("Tasks")` |
| Tooltip папки данных | путь временно некорректен для `Path.GetFullPath` | Getter не бросает исключение; tooltip возвращает безопасное значение |
| Remote выбран, URL пустой | `GitRemoteName = "origin"`, service URL найден | `GitRemoteUrl` заполняется URL remote |
| Remote выбран, URL уже задан | `GitRemoteUrl` непустой | URL не перезаписывается |
| Один remote, выбор пустой | `Remotes.Count == 1`, URL пустой | remote выбирается автоматически, URL заполняется |
| Видимость remote | `Remotes.Count <= 1` | Поле "Удаленный источник" всё равно видно в основном Git backup блоке |
| Ветка отправки | `GitPushRefSpec = "refs/heads/main"` | UI показывает `refs/heads/main` |
| Fallback ветки отправки | `GitPushRefSpec` пустой, `GitBranch = "master"` | `GitPushRefSpec` становится `refs/heads/master` |
| Sync readiness | backup включён, remote name + push ref заполнены, не busy | sync/pull/push доступны |
| Sync readiness | remote или push ref пустой | sync/pull/push недоступны |

## 8. Точки интеграции и триггеры
- Конструктор `SettingsViewModel` вызывает `ReloadGitMetadata()`, поэтому автозаполнение URL может сработать при открытии настроек.
- `GitRemoteNameDisplay` и `GitRemoteName` обновляют auth mode, backup state и availability.
- `GitPushRefSpec` при изменении обновляет availability; пустой push ref может быть инициализирован из `GitBranch` при конструировании ViewModel или загрузке metadata.
- Новая кнопка "Обновить" вызывает `RefreshGitMetadataCommand`, а команда вызывает `ReloadGitMetadata()`.
- `CloneCommand`, `PullCommand`, `PushCommand`, `SyncNowCommand` уже вызывают `ReloadGitMetadata()` после операций; это сохранится.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Добавляется computed property для tooltip полного пути.
- Для точного tooltip пустого пути ViewModel получает или использует эквивалентный resolver effective local storage path. Resolver обязан совпадать с текущими fallback-правилами `TaskStorageFactory`/`FileStorage`.
- Добавляется новый метод интерфейса сервиса; все реализации и тестовые fake-реализации должны быть обновлены.
- `Git.Branch` остаётся persisted, но больше не редактируется из основного блока настроек.
- `Git.PushRefSpec` при пустом значении получает минимальный compatibility fallback из `Git.Branch`.

## 10. Миграция / Rollout / Rollback
- При первом запуске после изменения уже сохранённые настройки читаются как раньше.
- Если `GitRemoteUrl` пустой и repository metadata доступна, поле будет заполнено из выбранного/единственного remote и сохранено в конфиг.
- Если `GitPushRefSpec` отсутствует или пустой, а `GitBranch` есть, `GitPushRefSpec` будет заполнен каноническим ref из `GitBranch` и сохранён в конфиг.
- Rollback: вернуть XAML-поля и прежнее правило `CanSyncRepository`; новые тесты удалять только вместе с rollback соответствующего поведения.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Tooltip поля "Папка с данными" показывает полный путь для относительного `TaskStoragePath`.
  - Tooltip поля "Папка с данными" показывает полный фактический fallback path даже при пустом `TaskStoragePath`.
  - В основной секции Git есть кнопка обновления метаданных, которая вызывает `ReloadGitMetadata()`.
  - Пустой `GitRemoteUrl` заполняется URL выбранного или единственного remote.
  - В основной секции вместо "Ветка" показана "Ветка отправки" в canonical ref формате, а вместо "Способ входа" показан всегда видимый "Удаленный источник".
  - В расширенных настройках не дублируются "Удаленный источник" и "Ветка отправки".
  - Ручные кнопки "Получить изменения сейчас" и "Отправить изменения сейчас" находятся внутри Git backup блока.
  - "Синхронизировать сейчас", "Получить изменения сейчас" и "Отправить изменения сейчас" доступны при заполненных remote name и push ref, если backup включён и нет busy-состояния.
  - Подписи интервалов явно содержат единицы измерения "сек.".
- Какие тесты добавить/изменить:
  - `SettingsViewModelTests`: full-path tooltip для относительного пути.
  - `SettingsViewModelTests`: full-path tooltip для пустого пути с fallback на actual storage default.
  - `SettingsViewModelTests`: tooltip path getter не бросает исключение на некорректном вводе.
  - `SettingsViewModelTests`: автозаполнение `GitRemoteUrl` из выбранного remote при пустом URL.
  - `SettingsViewModelTests`: existing URL не перезаписывается remote URL.
  - `SettingsViewModelTests`: `GitPushRefSpec` инициализируется canonical ref из `GitBranch`, если `GitPushRefSpec` пустой.
  - `SettingsViewModelTests`: `CanSyncRepository` зависит от remote name + push ref, а не от connected state.
  - XAML/build validation подтверждает, что `Удаленный источник` не скрыт через `HasMultipleRemotes`, а pull/push кнопки находятся в `GitBackupEnabled` блоке.
  - При необходимости адаптировать существующие тесты auth/readiness под новое правило.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`

## 12. Риски и edge cases
- `Path.GetFullPath` зависит от current directory процесса; для UI tooltip это ожидаемое поведение, но тест должен учитывать рабочий каталог.
- Пустой путь должен использовать тот же fallback, что фактическое подключение storage; если fallback-логика меняется, тест tooltip должен измениться вместе с ней.
- `Path.GetFullPath` может бросить исключение на отдельных невалидных строках; tooltip getter обязан быть безопасным для live input.
- Автозаполнение URL из remote сохраняет значение в конфиг; это намеренное поведение только когда поле было пустым.
- Если несколько remotes и ни один не выбран, ViewModel не должна угадывать URL.
- Если `GitBranch` уже содержит canonical `refs/...`, fallback для `GitPushRefSpec` не должен добавлять второй `refs/heads/`.
- Изменение `IRemoteBackupService` требует обновить все реализации и fake-классы.
- Если compiled bindings требуют property для новой команды, имя должно быть добавлено в `SettingsViewModel`.

## 13. План выполнения
1. Добавить failing regression-тесты для full path tooltip, empty-path fallback, remote URL autofill, `GitPushRefSpec` fallback и sync availability.
2. Обновить `IRemoteBackupService` и fake-service в тестах методом `GetRemoteUrl`.
3. Реализовать safe computed tooltip с actual storage fallback и remote URL autofill в `SettingsViewModel`.
4. Обновить canonical `GitPushRefSpec` fallback из `GitBranch` и правило `CanSyncRepository`.
5. Добавить `RefreshGitMetadataCommand` в ViewModel и назначение команды в `App.axaml.cs`.
6. Обновить `SettingsControl.axaml`: поля Git, всегда видимый remote, canonical branch field, кнопка обновления, единицы интервалов, отсутствие дублирования advanced-полей, перенос pull/push внутрь backup блока.
7. Запустить targeted tests, затем `dotnet build` и полный `dotnet test`.
8. Выполнить post-EXEC review и исправить найденные high-confidence проблемы.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - UI-поток не блокируется: обновление Git metadata остаётся синхронным lightweight read, долгие Git pull/push/clone уже выполняются через `Task.Run`.
  - Binding/команды экрана меняются точечно.
  - Стабильные test selectors не затрагиваются, новых automation-id сейчас нет.
  - План проверки включает `dotnet build` и `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | safe computed tooltip с storage fallback, remote URL autofill, canonical push ref fallback, refresh command property, sync readiness | Основная логика настроек |
| `src/Unlimotion.ViewModel/IRemoteBackupService.cs` | метод чтения URL remote | ViewModel нужен URL выбранного remote |
| `src/Unlimotion/Services/BackupViaGitService.cs` | реализация чтения URL remote | Источник repository metadata |
| `src/Unlimotion/Views/SettingsControl.axaml` | перестановка Git-полей, всегда видимый remote, canonical push ref, кнопка обновления, единицы интервалов, перенос pull/push | Выполнение UX-замечаний |
| `src/Unlimotion/App.axaml.cs` | назначение `RefreshGitMetadataCommand` | Кнопка обновления metadata |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | regression-тесты новых правил | Проверяемость поведения |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Tooltip папки данных | исходная строка пути | полный фактический путь, включая fallback при пустом поле |
| Git metadata refresh | только после операций/SSH refresh | отдельная кнопка обновления рядом с Git-настройками |
| Repository URL | вводится вручную или после clone | пустое поле заполняется из выбранного/единственного remote |
| Основное поле branch | `GitBranch` | `GitPushRefSpec` как "Ветка отправки" в canonical ref формате |
| Основное поле auth | readonly "Способ входа" | всегда видимый "Удаленный источник" |
| Ручные Git-действия | pull/push в advanced вне backup блока | pull/push внутри `GitBackupEnabled` блока |
| Sync readiness | URL + Connected | remote name + push ref + enabled + not busy |
| Интервалы | без единиц | подписи с "сек." |

## 18. Альтернативы и компромиссы
- Вариант: не расширять `IRemoteBackupService`, а парсить `.git/config` во ViewModel.
- Плюсы: меньше изменений интерфейса.
- Минусы: ViewModel начинает знать формат Git repository config и ломает разделение ответственности.
- Почему выбранное решение лучше в контексте этой задачи: сервис уже инкапсулирует libgit2 и Git metadata, поэтому чтение remote URL должно остаться там.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, правила, state и rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Границы, совместимость и rollback заданы. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, тесты и команды указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План есть, blockers отсутствуют, масштаб ограничен. |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client` требования отражены. |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Все 8 замечаний разложены на критерии и Non-Goals. |
| 2. Понимание текущего состояния | 5 | Указаны конкретные файлы, текущие bindings и текущая логика readiness. |
| 3. Конкретность целевого дизайна | 5 | Описаны properties, команда, сервисный метод и XAML-перестановка. |
| 4. Безопасность (миграция, откат) | 5 | Persisted модель сохраняется, rollback понятен. |
| 5. Тестируемость | 5 | Есть targeted tests и full validation commands. |
| 6. Готовность к автономной реализации | 5 | Открытых вопросов нет, план пошаговый. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлено явное правило для нескольких remotes без выбора, сохранение существующего `GitRemoteUrl`, не-дублирование remote/push branch в advanced; после пользовательского review уточнены перенос pull/push внутрь backup блока, canonical формат `GitPushRefSpec`, всегда видимый remote selector, safe tooltip actual path при пустом поле и fallback `GitBranch -> GitPushRefSpec`.
- Что осталось на решение пользователя: требуется подтверждение спеки фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: добавлен edge-case для stale selected remote при единственном фактическом remote; исправлен `EnsureSingleRemoteSelection()` и добавлен regression-тест.
- Что проверено дополнительно для refactor / comments: старые UI-поля `Способ входа`, самостоятельная `Ветка` и `HasMultipleRemotes` visibility не остались в `SettingsControl.axaml`; `git diff --check` без ошибок.
- Остаточные риски / follow-ups: в первом повторном запуске тестового проекта был transient сбой существующего Avalonia Headless UI-теста на dispose; повторный запуск тестового проекта и финальный `dotnet test src/Unlimotion.sln --no-build` прошли.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор instruction stack и кода настроек | 0.95 | Нет | Создать рабочую спецификацию | Да | Да, требуется подтверждение перед EXEC | Центральный QUEST-гейт требует SPEC-first и пользовательского approval | `specs/2026-04-21-settings-git-restyle-followups.md` |
| SPEC | Post-SPEC review | 0.95 | Нет | Ожидать фразу `Спеку подтверждаю` | Да | Да, approval ещё не получен | Спека прошла linter/rubric, блокирующих вопросов нет | `specs/2026-04-21-settings-git-restyle-followups.md` |
| SPEC | Интеграция решений пользователя по review | 0.95 | Нет | Ожидать фразу `Спеку подтверждаю` | Да | Да, пользователь дал решения по 5 review-пунктам | Спека уточнена перед EXEC, чтобы исключить неоднозначную реализацию | `specs/2026-04-21-settings-git-restyle-followups.md` |
| EXEC | Regression tests red-step | 0.9 | Нет | Реализовать недостающее поведение | Нет | Нет | Добавлены тесты tooltip, remote URL autofill, push ref fallback и sync readiness; targeted build падает на отсутствующем `TaskStoragePathTooltip`, что подтверждает red-step | `src/Unlimotion.Test/SettingsViewModelTests.cs` |
| EXEC | Реализация и targeted test project | 0.9 | Нет | Запустить build и full tests | Нет | Нет | Реализованы ViewModel/service/App/XAML изменения; `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build` прошёл 154 теста. Команда со старым `--filter` заменена в спеке, потому что текущий TUnit runner её не поддерживает | `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion.ViewModel/IRemoteBackupService.cs`, `src/Unlimotion/Services/BackupViaGitService.cs`, `src/Unlimotion/App.axaml.cs`, `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `specs/2026-04-21-settings-git-restyle-followups.md` |
| EXEC | Post-EXEC review и финальные проверки | 0.95 | Нет | Завершить задачу | Нет | Нет | После review добавлен stale-remote edge case; `dotnet build src/Unlimotion.sln` и `dotnet test src/Unlimotion.sln --no-build` прошли, `git diff --check` без ошибок | `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `specs/2026-04-21-settings-git-restyle-followups.md` |
