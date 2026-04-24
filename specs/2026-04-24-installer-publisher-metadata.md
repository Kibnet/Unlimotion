# Заполнение издателя при публикации установочников

## 0. Метаданные
- Тип (профиль): `delivery-task`; stack profile: `dotnet-desktop-client`
- Владелец: desktop release packaging
- Масштаб: small
- Целевой релиз / ветка: новая task-ветка от `main` (в репозитории нет ветки `master`)
- Ограничения:
  - До подтверждения спеки нельзя менять файлы проекта вне этой спецификации.
  - Нужно сохранить текущие release trigger'ы, имена артефактов и Velopack channels.
  - Нельзя затянуть в новую задачу текущие посторонние коммиты из `codex/fix-breadcrumb-emoji-rendering`.
  - В рабочем дереве уже есть локальное изменение `AGENTS.md`; оно должно быть сохранено при git-операциях.
  - UI не затрагивается, поэтому UI-тесты не требуются; обязательны .NET build/test и packaging smoke checks.
- Связанные ссылки:
  - `.github/workflows/windows-packaging.yml`
  - `.github/workflows/deb_packaging.yml`
  - `.github/workflows/osx-packaging.yml`
  - `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj`
  - `src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj`
  - `src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj`
  - `specs/2026-04-23-velopack-github-updates.md`

## 1. Overview / Цель
Нужно, чтобы при публикации desktop-установочников метаданные издателя были заполнены и консистентны, а работа велась из отдельной ветки, основанной на актуальной основной ветке репозитория.

## 2. Текущее состояние (AS-IS)
- Основная ветка репозитория сейчас называется `main`; локальной или удалённой ветки `master` нет.
- Текущая рабочая ветка `codex/fix-breadcrumb-emoji-rendering` находится на 3 коммита впереди `main`, и эти коммиты не относятся к задаче про installer publisher.
- В рабочем дереве есть незакоммиченное изменение `AGENTS.md`; прямой `rebase` с ним не пройдёт без временного сохранения.
- Windows, Linux и macOS release workflow'и собирают publish output и затем публикуют Velopack installers через `vpk pack`.
- Во всех трёх workflow publisher для Velopack сейчас захардкожен как `--packAuthors Kibnet`.
- Desktop `.csproj` не содержат явных shared metadata `Company` / `Authors`, поэтому нативные packaging path'и (`CreateDeb`, macOS app/pkg input metadata, publish output version info) не имеют единого явно заданного источника publisher metadata.

## 3. Проблема
Publisher metadata для desktop-установочников задана непоследовательно: Velopack workflow'и используют захардкоженный `packAuthors`, а проектные metadata для publish/native packaging не задают общий publisher, из-за чего published installers и связанные артефакты могут выходить без заполненного издателя или с расходящимися значениями.

## 4. Цели дизайна
- Разделение ответственности: держать publisher metadata рядом с desktop packaging, а не размазывать по unrelated проектам.
- Повторное использование: одно и то же значение должно использоваться всеми desktop packaging path'ами.
- Тестируемость: изменение должно проверяться через publish/build/test/smoke checks, без ручного UI.
- Консистентность: Windows/Linux/macOS installers должны публиковаться с одинаковым publisher.
- Обратная совместимость: не менять release triggers, артефакты, каналы обновлений и package ids.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем UX, runtime-поведение приложения и UI.
- Не вводим code signing, notarization, GPG-signing или другие схемы доверия.
- Не меняем naming release assets, `packId`, `packTitle`, `channel` и GitHub release flow.
- Не переносим в новую ветку текущие unrelated commits из `codex/fix-breadcrumb-emoji-rendering`.
- Не трогаем Android/iOS/server packaging.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Desktop/Directory.Build.props` (новый файл) -> единые desktop-level MSBuild metadata для `Company` / `Authors` / `Product`, применимые ко всем desktop build variants.
- `.github/workflows/windows-packaging.yml` -> использовать общий publisher value для Velopack Windows packaging.
- `.github/workflows/deb_packaging.yml` -> использовать общий publisher value для Velopack Linux packaging.
- `.github/workflows/osx-packaging.yml` -> использовать общий publisher value для Velopack macOS packaging.
- Git state -> создать новую task-ветку от `main`; локальное изменение `AGENTS.md` временно сохранить и вернуть после переключения ветки.

### 6.2 Детальный дизайн
- Добавить в `src/Unlimotion.Desktop/Directory.Build.props` desktop-scoped metadata:
  - `Company = Kibnet`
  - `Authors = Kibnet`
  - `Product = Unlimotion`
- Не менять existing project-specific properties в трёх desktop `.csproj`, если они не конфликтуют с новыми metadata.
- В каждом release workflow убрать literal `Kibnet` из `vpk pack --packAuthors` и заменить на единое env/property-backed значение `INSTALLER_PUBLISHER`, чтобы Velopack installer metadata не расходилась с project metadata.
- Git-часть выполнить безопасно:
  - зафиксировать, что пользователь просил `master`, но фактическая основная ветка на 2026-04-24 — `main`;
  - временно сохранить локальный diff `AGENTS.md`;
  - создать новую ветку от `main`, а не от текущей feature-ветки, чтобы не утащить 3 unrelated commits;
  - восстановить локальный `AGENTS.md` diff поверх новой ветки.
- Обработка ошибок:
  - если stash/pop даст конфликт по `AGENTS.md`, вручную сохранить пользовательскую правку без отката других изменений;
  - если packaging smoke check не может быть выполнен в текущей среде, явно зафиксировать причину и сохранить все доступные build/test проверки.
- Производительность не меняется: меняются только metadata и workflow arguments.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Publisher string для desktop installer publication должна быть одинаковой на Windows/Linux/macOS.
- Новая task-ветка должна строиться от `main`, если текущая активная ветка содержит unrelated коммиты и не является чистой базой для задачи.

## 8. Точки интеграции и триггеры
- `release.published` в трёх GitHub Actions workflow обязан использовать единый publisher value при вызове `vpk pack`.
- `dotnet publish` / `CreateDeb` / macOS app build должны получать desktop project metadata из общего desktop-level props-файла.

## 9. Изменения модели данных / состояния
- Новые persisted данные не добавляются.
- Добавляются только build/package metadata properties.

## 10. Миграция / Rollout / Rollback
- Поведение при первом запуске приложения не меняется.
- Обратная совместимость сохраняется: release workflow остаётся тем же по trigger'ам и asset naming.
- Откат: удалить desktop-level metadata props и вернуть literal publisher в workflow'и.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Новая рабочая ветка создана от `main`; unrelated commits из `codex/fix-breadcrumb-emoji-rendering` в неё не попали.
  - Во всех desktop release workflow publisher больше не захардкожен отдельно в трёх местах без общего обозначения.
  - Desktop packaging metadata задаёт `Company/Authors` единообразно для desktop проектов.
  - Локальный `AGENTS.md` diff сохранён после git-операций.
  - `dotnet build` и `dotnet test` проходят.
- Какие тесты добавить/изменить:
  - Новые UI/unit тесты не требуются: изменение инфраструктурное, без runtime/UI-поведения.
  - Обязательны automated command checks как regression coverage для packaging metadata.
- Characterization tests / contract checks для текущего поведения:
  - Проверить, что release workflow'и по-прежнему содержат те же `packId`, `packTitle`, `channel`, `mainExe`, trigger.
- Базовые замеры до/после для performance tradeoff:
  - Не применимо.
- Команды для проверки:
  - `git status --short --branch`
  - `git log --oneline main..HEAD`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`
  - `dotnet publish src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release -f net10.0 -r win-x64 -o .\\artifacts\\verify\\installer-publisher\\win -p:PublishSingleFile=true --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:Version=0.0.1-local --ignore-failed-sources`
  - `powershell -NoProfile -Command "(Get-Item '.\\artifacts\\verify\\installer-publisher\\win\\Unlimotion.Desktop.exe').VersionInfo | Format-List CompanyName,ProductName,FileVersion"`
  - `git diff --check`

## 12. Риски и edge cases
- `main` вместо `master`: пользовательский запрос использует несуществующее имя ветки; нужно явно сообщить о фактической базе.
- Локальный `AGENTS.md` diff может конфликтовать при stash/pop или при checkout новой ветки.
- `dotnet test src/Unlimotion.sln` может падать по уже известным внешним причинам; если так, нужно отделить новый regression от pre-existing failures.
- Если `Packaging.Targets` не читает `Authors/Company` так, как ожидается, минимально допустимый outcome всё равно должен обеспечивать заполненный publisher для publish output и Velopack installers.

## 13. План выполнения
1. После подтверждения спеки временно сохранить локальный diff `AGENTS.md`.
2. Переключиться на `main`, подтянуть актуальное состояние `origin/main`, создать новую ветку для задачи от `main`.
3. Вернуть локальный `AGENTS.md` diff и убедиться, что unrelated commits не перешли в новую ветку.
4. Добавить desktop-level metadata props для publisher/author/company.
5. Обновить Windows/Linux/macOS workflow'и, чтобы использовать общий publisher value в `vpk pack`.
6. Выполнить targeted packaging smoke check, затем `dotnet build` и полный `dotnet test`.
7. Провести post-EXEC review, поправить найденные high-confidence проблемы, повторить затронутые проверки.

## 14. Открытые вопросы
- Нет. Объективно лучшая база для новой ветки — `main`, потому что `master` отсутствует, а текущая ветка уже содержит unrelated commits.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - Изменение ограничено desktop packaging.
  - UI/navigation/runtime flow не меняются.
  - В финальной EXEC-фазе будут обязательны `dotnet build` и `dotnet test`.
  - UI tests не требуются, потому что нет UI-facing behavior change; это отдельно зафиксировано как ограничение.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Desktop/Directory.Build.props` | Новый desktop-scoped metadata props | Единый источник `Company` / `Authors` / `Product` |
| `.github/workflows/windows-packaging.yml` | Общий publisher для `vpk pack` | Консистентный Windows installer publisher |
| `.github/workflows/deb_packaging.yml` | Общий publisher для `vpk pack` | Консистентный Linux installer publisher |
| `.github/workflows/osx-packaging.yml` | Общий publisher для `vpk pack` | Консистентный macOS installer publisher |
| `specs/2026-04-24-installer-publisher-metadata.md` | Рабочая спецификация и журнал | Аудит решения и проверок |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Основная ветка | Пользовательский запрос указывает `master`, ветка отсутствует | Работа ведётся от фактической основной ветки `main` |
| База новой задачи | Текущая ветка на 3 unrelated коммита впереди `main` | Новая ветка создаётся от `main` без посторонней истории |
| Velopack publisher | `Kibnet` захардкожен в трёх workflow | Общий publisher value в release workflow'ах |
| Desktop project metadata | Нет shared `Company/Authors` | Единые desktop-scoped metadata |

## 18. Альтернативы и компромиссы
- Вариант: поменять только `--packAuthors` в workflow'ах.
- Плюсы: минимальный diff.
- Минусы: native packaging/publish metadata останутся без общего publisher source.
- Почему выбранное решение лучше в контексте этой задачи: оно закрывает и workflow publication, и desktop project metadata, не затрагивая runtime и не требуя тяжёлой переработки packaging pipeline.

- Вариант: создавать новую ветку от текущей `codex/fix-breadcrumb-emoji-rendering` и делать `rebase` на `main`.
- Плюсы: формально ближе к буквальной формулировке пользователя.
- Минусы: в новую задачу попадут 3 unrelated commit'а.
- Почему выбранное решение лучше в контексте этой задачи: новая ветка сразу от `main` даёт чистую историю и минимальный риск случайно доставить чужие изменения.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, границы и non-goals описаны. |
| B. Качество дизайна | 6-10 | PASS | Зафиксированы ответственность, интеграция, rollback и обработка ошибок. |
| C. Безопасность изменений | 11-13 | PASS | Отдельно учтены `main` vs `master`, stash локального diff и отказ от unrelated commits. |
| D. Проверяемость | 14-16 | PASS | Есть acceptance criteria и набор команд для build/test/publish smoke. |
| E. Готовность к автономной реализации | 17-19 | PASS | План по шагам есть, блокирующих вопросов нет, выбран объективно лучший вариант базы ветки. |
| F. Соответствие профилю | 20 | PASS | Изменение соответствует desktop packaging без UI-behavior change. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Цель и non-goals сформулированы проверяемо. |
| 2. Понимание текущего состояния | 5 | Зафиксированы текущие workflow'и, отсутствие `master`, ahead-of-main состояние и локальный diff. |
| 3. Конкретность целевого дизайна | 5 | Указаны конкретные файлы, свойства и шаги workflow. |
| 4. Безопасность (миграция, откат) | 5 | Есть план stash/create-branch/rollback и ограничения по истории. |
| 5. Тестируемость | 5 | Прописаны build/test/publish smoke checks и acceptance criteria. |
| 6. Готовность к автономной реализации | 5 | Открытых вопросов нет, решение small и локализовано. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - Явно зафиксировано, что на 2026-04-24 в репозитории нет `master`, а есть `main`.
  - Добавлен отдельный риск по локальному изменению `AGENTS.md`, которое помешает git-операциям.
  - Уточнено, что новая ветка должна стартовать от `main`, а не от текущей unrelated feature-ветки.
- Что осталось на решение пользователя:
  - Нет.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения:
  - После первого неудачного запуска `dotnet test src/Unlimotion.sln --no-build -m:1` убран несовместимый флаг `-m`, потому что текущий TUnit runner интерпретирует его как неизвестный `--m`.
- Что проверено дополнительно для refactor / comments:
  - Проверено, что изменены только desktop packaging metadata и аргументы `vpk pack`; runtime/UI код не тронут.
  - Проверено, что `CompanyName` publish output теперь равен `Kibnet`, а `ProductName` равен `Unlimotion`.
- Остаточные риски / follow-ups:
  - В проекте остаются существующие dependency/build warnings (`NU1903`, `NU1904`, `NU1608`, preview SDK warnings, CRLF warnings в workflow files), но они не вызваны этим изменением.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор контекста по release packaging и git-базе | 0.95 | Нужно только подтверждение спецификации для перехода в EXEC | Запросить подтверждение спеки | Да | Нет | Проверены workflow'и, desktop csproj, состояние веток и локальный diff; этого достаточно для безопасного плана реализации. | `.github/workflows/windows-packaging.yml`, `.github/workflows/deb_packaging.yml`, `.github/workflows/osx-packaging.yml`, `src/Unlimotion.Desktop/*.csproj`, `AGENTS.md`, `specs/2026-04-24-installer-publisher-metadata.md` |
| EXEC | Подготовка git-базы | 0.98 | Нет | Внести правки в packaging metadata | Нет | Да, пользователь подтвердил spec фразой `Спеку подтверждаю` | Локальные `AGENTS.md` и spec временно перенесены через stash; создана новая ветка `codex/installer-publisher-metadata` от `origin/main`, чтобы не перенести 3 unrelated commit'а из прежней ветки. | `AGENTS.md`, `specs/2026-04-24-installer-publisher-metadata.md`, git refs |
| EXEC | Реализация installer publisher metadata | 0.94 | Нужны результаты build/test/publish checks | Запустить проверки | Нет | Нет | Добавлен desktop-scoped источник `Company/Authors/Product`, а Velopack workflow'и переведены на общий `INSTALLER_PUBLISHER`, чтобы publisher был согласован для publish/install flows. | `src/Unlimotion.Desktop/Directory.Build.props`, `.github/workflows/windows-packaging.yml`, `.github/workflows/deb_packaging.yml`, `.github/workflows/osx-packaging.yml`, `specs/2026-04-24-installer-publisher-metadata.md` |
| EXEC | Верификация и post-EXEC review | 0.97 | Нет | Финализировать ответ | Нет | Нет | `dotnet test src/Unlimotion.sln --no-build` прошёл: 205/205, `dotnet build src/Unlimotion.sln --no-restore` прошёл, Windows publish smoke подтвердил `CompanyName=Kibnet` и `ProductName=Unlimotion`, `git diff --check` не выявил diff errors. Отдельно зафиксирован нюанс: TUnit runner не принимает `-m:1` через `dotnet test`. | `src/Unlimotion.sln`, `src/Unlimotion.Desktop/Directory.Build.props`, `.github/workflows/windows-packaging.yml`, `.github/workflows/deb_packaging.yml`, `.github/workflows/osx-packaging.yml`, `artifacts/verify/installer-publisher/win`, `specs/2026-04-24-installer-publisher-metadata.md` |
| EXEC | Фиксация результата в git | 0.99 | Нет | Сделать коммит scoped-изменений | Нет | Да, пользователь попросил `сделай коммит` | В коммит входят только workflow'и, новый desktop metadata props и рабочая spec; пользовательский локальный diff `AGENTS.md` сохраняется вне индекса. | `.github/workflows/windows-packaging.yml`, `.github/workflows/deb_packaging.yml`, `.github/workflows/osx-packaging.yml`, `src/Unlimotion.Desktop/Directory.Build.props`, `specs/2026-04-24-installer-publisher-metadata.md`, `AGENTS.md` |
