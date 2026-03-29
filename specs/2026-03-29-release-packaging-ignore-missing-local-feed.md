# Починка deb и macOS packaging build при отсутствии локального Android feed

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client`
- Владелец: Unlimotion release packaging
- Масштаб: `small`
- Целевой релиз / ветка: `main`
- Ограничения:
  - Не менять `src/nuget.config`, чтобы не ломать Android/Termux сценарий.
  - Не менять release naming, upload steps и состав desktop artifacts.
  - Исправлять только `deb` и `macOS` packaging flows.
- Связанные ссылки:
  - `AGENTS.md`
  - `.github/workflows/deb_packaging.yml`
  - `.github/workflows/osx-packaging.yml`
  - `src/nuget.config`
  - `src/Directory.Packages.props`
  - `src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj`
  - `src/Unlimotion.Desktop/ci/deb/generate-deb-pkg.sh`
  - `src/Unlimotion.Desktop/ci/osx/generate-osx-publish.sh`

## 1. Overview / Цель
Починить оставшиеся release workflows `Unlimotion debPkg` и `Unlimotion macOSPkg`, которые падали на GitHub Actions из-за отсутствующего локального source `/storage/emulated/0/nuget-local`.

## 2. Текущее состояние (AS-IS)
- В `src/nuget.config` есть Android-only source `/storage/emulated/0/nuget-local`.
- На Linux/macOS/Windows runners этот source отсутствует и приводит к `NU1301` без `--ignore-failed-sources`.
- `macOS` flow падал в `Publish`.
- `deb` flow падал в `Generate DebPkg`.
- Репо использует central package management через `src/Directory.Packages.props`.
- Runtime вызов `dotnet-deb install` генерирует `Directory.Build.props` с `PackageReference Version=...`, что конфликтует с CPM и даёт `NU1008`.

## 3. Проблема
Release packaging scripts опираются на environment-specific feed и хрупкий runtime setup `dotnet-deb`, из-за чего desktop release workflows не воспроизводятся надёжно на CI runners.

## 4. Цели дизайна
- Убрать hard-fail на missing local source.
- Сделать deb packaging совместимым с central package management.
- Сохранить Android/Termux поток без изменений.
- Оставить изменения минимальными и локальными к packaging flow.

## 5. Non-Goals (чего НЕ делаем)
- Не удаляем локальный source из `src/nuget.config`.
- Не меняем Android build notes или mobile pipeline.
- Не трогаем `msi_packaging.yml` в рамках этой спеки.
- Не исправляем существующие warnings в кодовой базе.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
| Компонент / файл | Ответственность |
| --- | --- |
| `src/Unlimotion.Desktop/ci/osx/generate-osx-publish.sh` | Игнорировать missing local source в `restore/publish`. |
| `src/Directory.Packages.props` | Зафиксировать версию `Packaging.Targets` для CPM-совместимого deb packaging. |
| `src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj` | Подключить `Packaging.Targets` как build-time dependency. |
| `src/Unlimotion.Desktop/ci/deb/generate-deb-pkg.sh` | Убрать runtime `dotnet-deb install`, добавить ignore flags и явный `RuntimeIdentifier=linux-x64`. |

### 6.2 Детальный дизайн
- Для `macOS`:
  - `dotnet restore ... --ignore-failed-sources`
  - `dotnet publish ... --ignore-failed-sources`
- Для `deb`:
  - `Packaging.Targets` подключается через CPM и `PackageReference`, а не через runtime install.
  - `dotnet restore ... --runtime linux-x64 --ignore-failed-sources`
  - `dotnet msbuild ... -t:CreateDeb -p:RuntimeIdentifier=linux-x64 -p:RestoreIgnoreFailedSources=true`
- Missing local source должен становиться warning, а не причиной fail.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Android-local source остаётся допустимым в repo config, пока desktop packaging умеет переживать его отсутствие.
- Deb packaging не должен модифицировать project tree на лету для подключения targets.

## 8. Точки интеграции и триггеры
- `.github/workflows/deb_packaging.yml` -> step `Generate DebPkg`
- `.github/workflows/osx-packaging.yml` -> step `Publish`

## 9. Изменения модели данных / состояния
- Нет изменений persisted data.
- Нет изменений runtime state приложения.
- Меняются только packaging/build-time зависимости и shell scripts.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - обновить `deb` и `osx` scripts
  - добавить `Packaging.Targets` в CPM и Debian csproj
- Rollback:
  - убрать `Packaging.Targets` из проекта и откатить shell scripts

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
1. `dotnet restore` для `Unlimotion.Desktop.ForMacBuild.csproj` проходит с `--ignore-failed-sources`.
2. `dotnet publish` для `Unlimotion.Desktop.ForMacBuild.csproj` проходит с `--ignore-failed-sources`.
3. `dotnet restore` для `Unlimotion.Desktop.ForDebianBuild.csproj --runtime linux-x64` проходит с `--ignore-failed-sources`.
4. `dotnet msbuild -t:CreateDeb` для `Unlimotion.Desktop.ForDebianBuild.csproj` проходит с `RestoreIgnoreFailedSources=true`.

### Команды для проверки
```powershell
dotnet restore src\Unlimotion.Desktop\Unlimotion.Desktop.ForMacBuild.csproj --ignore-failed-sources
dotnet publish src\Unlimotion.Desktop\Unlimotion.Desktop.ForMacBuild.csproj -c Release -f net10.0 -r osx-x64 -p:Version=0.0.0-local -p:PublishSingleFile=true --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --ignore-failed-sources
dotnet restore src\Unlimotion.Desktop\Unlimotion.Desktop.ForDebianBuild.csproj --runtime linux-x64 --ignore-failed-sources
dotnet msbuild src\Unlimotion.Desktop\Unlimotion.Desktop.ForDebianBuild.csproj -t:CreateDeb -p:Version=0.0.0-local -p:Configuration=Release -p:TargetFramework=net10.0 -p:RuntimeIdentifier=linux-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:RestoreIgnoreFailedSources=true
```

## 12. Риски и edge cases
- Если package реально доступен только из локального feed, build всё равно должен падать корректно.
- `dotnet-deb install` больше не используется, чтобы не ловить `NU1008` под CPM.
- На реальном runner может всплыть следующая независимая ошибка уже после packaging fix.

## 13. План выполнения
1. Обновить `generate-osx-publish.sh`.
2. Подключить `Packaging.Targets` через CPM и Debian csproj.
3. Упростить `generate-deb-pkg.sh` до `restore + CreateDeb`.
4. Прогнать локальные `restore/publish/msbuild` проверки.

## 14. Открытые вопросы
Блокирующих вопросов нет.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - изменение ограничено desktop packaging flow
  - проверка выполнена точечными `dotnet` командами
  - бизнес-логика и UI не затрагиваются

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Directory.Packages.props` | Добавлена версия `Packaging.Targets` | Совместимый с CPM deb packaging |
| `src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj` | Добавлен `PackageReference` на `Packaging.Targets` | Убрать runtime install side-effects |
| `src/Unlimotion.Desktop/ci/deb/generate-deb-pkg.sh` | Убран `dotnet-deb install`, добавлены ignore flags и `RuntimeIdentifier` | Стабильный deb packaging в CI |
| `src/Unlimotion.Desktop/ci/osx/generate-osx-publish.sh` | Добавлены ignore flags | Стабильный macOS publish в CI |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `osx` packaging | Падает на missing local source | Игнорирует missing source и публикуется дальше |
| `deb` packaging feed handling | Падает на missing local source | Игнорирует missing source |
| `deb` packaging target setup | Runtime `dotnet-deb install` конфликтует с CPM | `Packaging.Targets` подключён как обычная build-time зависимость |

## 18. Альтернативы и компромиссы
- Вариант: удалить локальный source из `src/nuget.config`
- Плюсы:
  - проблема пропадает во всех desktop workflows
- Минусы:
  - ломает Android/Termux сценарий
- Почему выбранное решение лучше в контексте этой задачи:
  - минимальный blast radius
  - фиксирует конкретно desktop packaging
  - не требует environment-specific runtime setup

## 19. Результат прогона линтера
- SPEC Linter: PASS
- SPEC Rubric: готово к автономной реализации

## Approval
Спека подтверждена пользователем 29 марта 2026.
