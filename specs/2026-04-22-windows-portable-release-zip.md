# Windows portable zip artifact for release workflow

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`; контекст `testing-dotnet`
- Владелец: Codex
- Масштаб: small
- Целевой релиз / ветка: `feature/windows-portable-zip-release` в worktree `C:\Projects\Education\Unlimotion Space\Unlimotion-windows-portable-zip`
- Ограничения:
  - До подтверждения спеки менять только этот файл.
  - Не менять runtime-код приложения и формат пользовательских данных.
  - Не ломать текущую MSI-сборку и имя существующего MSI artifact.
  - Portable zip должен собираться при `release.published`, как и остальные release packages.
- Связанные ссылки:
  - `AGENTS.md`
  - `C:\Projects\My\Agents\instructions\core\quest-governance.md`
  - `C:\Projects\My\Agents\instructions\core\quest-mode.md`
  - `.github/workflows/msi_packaging.yml`
  - `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj`
  - `global.json`

## 1. Overview / Цель
Добавить в GitHub Actions release flow Windows portable zip package, чтобы при публикации релиза вместе с MSI появлялся архив для запуска без установки.

## 2. Текущее состояние (AS-IS)
- Windows release packaging живет в `.github/workflows/msi_packaging.yml`.
- Workflow запускается на событие:
  - `release`
  - `types: [published]`
- Job `advinst-aip-build` выполняется на `windows-2022`.
- В шаге `Publish` уже выполняется:
  - `dotnet publish src\Unlimotion.Desktop\Unlimotion.Desktop.csproj`
  - configuration `Release`
  - target framework `net10.0`
  - runtime `win-x64`
  - self-contained publish
  - `PublishSingleFile=true`
  - output directory `${{ github.workspace }}\src\Unlimotion.Desktop\bin\Release\net10.0\win-x64\publish`
- Этот publish output затем используется Advanced Installer для MSI.
- Сейчас release asset для Windows загружается только как `Unlimotion-${{ github.ref_name }}.msi`.

## 3. Проблема
Пользователю, которому нужна портативная Windows-версия, приходится устанавливать MSI или самостоятельно извлекать/собирать publish output. Release workflow не публикует готовый zip asset.

## 4. Цели дизайна
- Разделение ответственности: существующий `Publish` продолжает готовить Windows build output, новый шаг только упаковывает его в zip.
- Повторное использование: не запускать второй `dotnet publish`, а переиспользовать уже созданный `win-x64\publish`.
- Тестируемость: локально проверить `dotnet publish` и zip packaging команду, а также синтаксис/состав workflow.
- Консистентность: имя zip должно совпадать с release naming convention и явно показывать платформу/portable nature.
- Обратная совместимость: текущий MSI artifact и Advanced Installer flow остаются без изменения поведения.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем auto-update/install logic.
- Не меняем Advanced Installer `.aip` и MSI package metadata.
- Не добавляем Windows x86/arm64 portable packages.
- Не меняем macOS, deb или Android workflows.
- Не включаем signing/notarization изменения.
- Не меняем `.csproj` publish properties, если текущий output уже пригоден для zip.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
| Компонент / файл | Ответственность |
| --- | --- |
| `.github/workflows/msi_packaging.yml` | После `Publish` создать portable zip из publish output и загрузить его в release assets. |
| `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj` | Остается источником Windows desktop publish output. |

### 6.2 Детальный дизайн
- В `msi_packaging.yml` добавить переменные окружения для повторно используемых путей/имен:
  - `WINDOWS_PUBLISH_DIR`
  - `WINDOWS_PORTABLE_DIR`
  - `WINDOWS_PORTABLE_ZIP`
- После `Publish` добавить PowerShell step `Create Portable Zip`:
  - убедиться, что publish directory существует;
  - создать отдельный каталог `${{ github.workspace }}\artifacts\windows-portable`;
  - выполнить `Compress-Archive` для содержимого publish directory;
  - использовать имя `Unlimotion-${{ github.ref_name }}-win-x64-portable.zip`.
- Добавить upload step для zip в release assets:
  - использовать текущий `xresloader/upload-to-github-release@v1.3.11` для единообразия внутри Windows workflow;
  - `file` должен ссылаться на `${{ env.WINDOWS_PORTABLE_ZIP }}`;
  - `tags: true`, как в MSI upload step.
- Предпочтение для EXEC: использовать минимальное изменение в текущем Windows workflow и не менять action для MSI, чтобы снизить риск регрессии.
- Порядок шагов:
  1. `Checkout`
  2. `Setup .NET 10 SDK`
  3. `Publish`
  4. `Create Portable Zip`
  5. `Build AIP`
  6. `Upload MsiPkg to Release`
  7. `Upload Portable Zip to Release`
- Если `Publish` не создал output, zip step должен падать явно, а не публиковать пустой архив.

## 7. Бизнес-правила / Алгоритмы
- Zip package должен содержать содержимое publish directory, а не вложенную папку с абсолютным/CI-specific путем.
- Release asset name должен быть стабильным и отличимым от MSI:
  - MSI: `Unlimotion-${{ github.ref_name }}.msi`
  - Portable zip: `Unlimotion-${{ github.ref_name }}-win-x64-portable.zip`
- Событие сборки остается `release.published`.
- Тег релиза берется из `github.ref_name`, как в текущей MSI-сборке.

## 8. Точки интеграции и триггеры
- Trigger: `.github/workflows/msi_packaging.yml` на `release.published`.
- Интеграция с build output: step `Publish`.
- Интеграция с GitHub Release assets: новый upload step после создания zip.

## 9. Изменения модели данных / состояния
- Persisted data приложения не меняется.
- Runtime state приложения не меняется.
- GitHub Release получит дополнительный asset.
- CI workspace получит временный файл `artifacts\windows-portable\Unlimotion-${{ github.ref_name }}-win-x64-portable.zip`.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - обновить `.github/workflows/msi_packaging.yml`;
  - проверить локальные команды publish/zip;
  - после merge следующий опубликованный release начнет получать zip asset.
- Rollback:
  - удалить zip creation/upload steps и связанные env-переменные из workflow;
  - MSI flow останется прежним.
- Обратная совместимость:
  - существующие consumers MSI не затрагиваются;
  - имена старых assets не меняются.

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
1. При `release.published` Windows workflow по-прежнему собирает и загружает `Unlimotion-${{ github.ref_name }}.msi`.
2. Тот же workflow дополнительно создает zip `Unlimotion-${{ github.ref_name }}-win-x64-portable.zip`.
3. Zip создается из содержимого `src\Unlimotion.Desktop\bin\Release\net10.0\win-x64\publish`.
4. Zip upload падает, если zip файл не создан.
5. Изменения не затрагивают macOS, deb и Android workflows.

### Какие тесты добавить/изменить
- Автоматические unit tests не нужны, потому что runtime-код не меняется.
- Нужны локальные command checks для publish output и zip creation.
- Нужна ручная проверка YAML diff и путей в workflow.

### Команды для проверки
```powershell
dotnet publish src\Unlimotion.Desktop\Unlimotion.Desktop.csproj -c Release -f net10.0 -r win-x64 -o .\artifacts\verify\windows-portable\publish -p:PublishSingleFile=true --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:Version=0.0.0-local --ignore-failed-sources
New-Item -ItemType Directory -Force .\artifacts\verify\windows-portable | Out-Null
Compress-Archive -Path .\artifacts\verify\windows-portable\publish\* -DestinationPath .\artifacts\verify\windows-portable\Unlimotion-0.0.0-local-win-x64-portable.zip -Force
if (-not (Test-Path .\artifacts\verify\windows-portable\Unlimotion-0.0.0-local-win-x64-portable.zip)) { throw "Portable zip was not created" }
dotnet build src\Unlimotion.sln
dotnet test src\Unlimotion.sln --no-build
```

## 12. Риски и edge cases
- `Compress-Archive` должен архивировать содержимое publish directory, иначе внутри zip появится лишняя папка `publish`.
- Если publish output содержит несколько файлов рядом с single-file exe, они должны попасть в zip.
- Zip нельзя хранить в `setup`, потому что Advanced Installer step тоже использует этот каталог для MSI output и может менять его содержимое.
- Full `dotnet build/test` может потребовать workload для Android/iOS; если локальная среда не готова, это нужно явно зафиксировать в EXEC отчете и заменить на максимально близкую targeted проверку.
- Если GitHub action upload path использует backslashes, нужно сохранить совместимость с Windows runner.
- Upload action для MSI уже отличается от других workflows; менять его без необходимости нельзя.

## 13. План выполнения
1. После фразы `Спеку подтверждаю` обновить `.github/workflows/msi_packaging.yml`.
2. Добавить zip creation step после `Publish`.
3. Добавить upload step для portable zip после MSI upload, используя `xresloader/upload-to-github-release@v1.3.11`.
4. Проверить локально `dotnet publish` и `Compress-Archive`.
5. Запустить `dotnet build src\Unlimotion.sln` и `dotnet test src\Unlimotion.sln --no-build` либо зафиксировать объективный blocker среды.
6. Выполнить post-EXEC review: сверить diff со спекой, проверить имена artifacts, отсутствие изменений других workflows и stale comments.

## 14. Открытые вопросы
Блокирующих вопросов нет.

Принятые допущения:
- Достаточно `win-x64` portable zip, потому что текущий Windows MSI flow уже публикует `win-x64`.
- Portable zip должен использовать тот же self-contained single-file output, что и MSI.
- Имя asset с суффиксом `-win-x64-portable.zip` достаточно явно для пользователей релиза.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - UI-поток и runtime-код не затрагиваются.
  - Platform-specific packaging остается в release workflow.
  - План проверки включает `dotnet publish`, `dotnet build` и `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `.github/workflows/msi_packaging.yml` | Добавить создание и upload Windows portable zip | Публиковать portable Windows package при релизе |
| `specs/2026-04-22-windows-portable-release-zip.md` | Рабочая спецификация и журнал | QUEST gate перед реализацией |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Windows release assets | Только MSI | MSI + `win-x64` portable zip |
| Windows publish output | Используется MSI build | Используется MSI build и zip packaging |
| Release trigger | `release.published` | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: отдельный workflow/job для portable zip.
- Плюсы:
  - независимая сборка zip от MSI/Advanced Installer.
- Минусы:
  - второй `dotnet publish`, больше времени CI и риск расхождения publish flags.
- Почему выбранное решение лучше в контексте этой задачи:
  - текущий MSI workflow уже производит нужный Windows publish output;
  - zip является упаковкой того же output;
  - минимальный blast radius и меньше новых точек отказа.

- Вариант: заменить MSI upload action или загрузить zip через `softprops/action-gh-release`.
- Плюсы:
  - единый upload action, glob/list files.
- Минусы:
  - меняет уже работающий MSI upload path.
- Почему выбранное решение лучше в контексте этой задачи:
  - MSI upload оставляем как есть; новый zip upload добавляется отдельно.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, правила asset naming и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Runtime/data не меняются, rollback прост, scope ограничен одним workflow. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria и команды проверки указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План малый, блокирующих вопросов нет, альтернативы рассмотрены. |
| F. Соответствие профилю | 20 | PASS | Desktop packaging проверяется через .NET publish/build/test; UI не затрагивается. |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Требуется конкретный release asset без изменения runtime-кода. |
| 2. Понимание текущего состояния | 5 | Указан текущий Windows workflow, publish flags и upload MSI. |
| 3. Конкретность целевого дизайна | 5 | Описаны шаги, пути, имя zip и порядок workflow. |
| 4. Безопасность (миграция, откат) | 5 | Изменение обратимо удалением новых шагов. |
| 5. Тестируемость | 5 | Есть publish/zip/build/test команды и acceptance criteria. |
| 6. Готовность к автономной реализации | 5 | Блокирующих вопросов нет, выбран минимальный вариант. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: zip destination перенесен из `setup` в отдельный `artifacts\windows-portable`; зафиксирован upload action `xresloader/upload-to-github-release@v1.3.11`; проверочная `Test-Path` команда заменена на fail-fast проверку; уточнен журнал действий агента.
- Что осталось на решение пользователя: требуется только подтверждение спеки фразой `Спеку подтверждаю`.

### EXEC Verification
| Команда / проверка | Результат | Комментарий |
| --- | --- | --- |
| `git diff --check` | PASS | Ошибок whitespace нет; Git предупреждает, что LF в `.github/workflows/msi_packaging.yml` будет заменен на CRLF при следующем касании. |
| `dotnet publish src\Unlimotion.Desktop\Unlimotion.Desktop.csproj ...` | BLOCKED | Команда не завершилась за 10 минут и была остановлена таймаутом инструмента без вывода. |
| `dotnet restore src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --runtime win-x64 --ignore-failed-sources -v minimal` | BLOCKED | Отдельный restore также не завершился за 5 минут и был остановлен таймаутом инструмента без вывода. |
| PowerShell zip logic на временном publish output | PASS | Архив создан, содержит `Unlimotion.Desktop.exe` и `dependency.dll` в корне, без вложенной папки `publish`. |
| `dotnet build src\Unlimotion.sln --no-restore` | BLOCKED | Быстро падает с `NETSDK1004` из-за отсутствующих `project.assets.json`; требуется успешный restore. |
| `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-build --no-restore` | BLOCKED | `dotnet test` сообщает `Тестовые проекты не найдены`, так как проект не восстановлен/не собран. |

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: дополнительных правок не потребовалось.
- Что проверено дополнительно для refactor / comments: runtime-код и комментарии не менялись; diff затрагивает только release workflow и рабочую спецификацию.
- Остаточные риски / follow-ups: локальный restore/publish/build/test не удалось завершить из-за зависания `dotnet restore` в текущей среде; фактический release workflow должен подтвердить publish и upload на GitHub Actions runner.

## Approval
Спека подтверждена пользователем 2026-04-22 фразой "Спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Подготовка worktree | 0.95 | Нет | Оформить спецификацию | Нет | Нет | Создан отдельный worktree и ветка для изоляции задачи от `main`. | `C:\Projects\Education\Unlimotion Space\Unlimotion-windows-portable-zip` |
| SPEC | Анализ release packaging | 0.9 | Нет | Запросить подтверждение спеки | Да | Обращение к человеку сделано; решение еще не получено | Windows MSI workflow уже имеет publish output, поэтому portable zip можно собрать из него без второго publish. | `.github/workflows/msi_packaging.yml`, `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj`, `specs/2026-04-22-windows-portable-release-zip.md` |
| SPEC | Review спеки | 0.95 | Нет | Запросить подтверждение спеки | Да | Обращение к человеку сделано; решение еще не получено | Устранены review-находки: отдельный каталог для zip, выбран upload action, проверка zip теперь fail-fast. | `specs/2026-04-22-windows-portable-release-zip.md` |
| EXEC | Подтверждение спеки | 1.0 | Нет | Обновить release workflow | Нет | Пользователь подтвердил фразой `Спеку подтверждаю` | Разрешен переход из SPEC в EXEC по правилам QUEST. | `specs/2026-04-22-windows-portable-release-zip.md` |
| EXEC | Изменение workflow | 0.95 | Нет | Запустить проверки | Нет | Нет | В Windows release workflow добавлены env-пути, создание portable zip из publish output и upload zip через текущий release upload action. | `.github/workflows/msi_packaging.yml`, `specs/2026-04-22-windows-portable-release-zip.md` |
| EXEC | Проверки | 0.8 | Полный локальный restore/publish не завершился в текущей среде | Выполнить post-EXEC review | Нет | Нет | Проверены diff whitespace и zip-логика; .NET publish/restore/build/test заблокированы локальным restore. | `.github/workflows/msi_packaging.yml`, `specs/2026-04-22-windows-portable-release-zip.md` |
| EXEC | Post-EXEC review | 0.9 | Нет | Завершить отчет пользователю | Нет | Нет | Diff соответствует спеке, новые шаги не трогают MSI upload и используют отдельный каталог для zip. | `.github/workflows/msi_packaging.yml`, `specs/2026-04-22-windows-portable-release-zip.md` |
